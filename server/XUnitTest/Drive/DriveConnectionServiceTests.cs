using System.Net;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Utility.DomainService.Drive;
using Utility.DomainService.Drive.Models;
using Utility.DomainService.Drive.Service;
using Utility.DomainService.Drive.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Drive;

/// <summary>
/// Guards the connect / callback / status / disconnect lifecycle (AC-1 to AC-9).
/// </summary>
/// <remarks>
/// The customer-visible bugs here are an open redirect through the callback, a replayed callback
/// that overwrites a live connection, a dead refresh token that is retried forever instead of
/// prompting a reconnect, and a rotated refresh token that is not stored so personal accounts
/// disconnect after an hour.
/// </remarks>
public sealed class DriveConnectionServiceTests : IDisposable
{
    private readonly ScriptedHttpHandler _http = new();
    private readonly Mock<IDriveRepository> _repository = new();
    private readonly Mock<ITenants> _tenants = new();
    private readonly ControlledTimeProvider _clock = DriveTestSupport.Clock();
    private readonly DriveMetrics _metrics = new();
    private readonly DriveConnectionService _service;

    private DriveConnection? _stored;
    private DriveOAuthState? _state;

    public DriveConnectionServiceTests()
    {
        DriveTestSupport.SetCaller();

        _tenants.Setup(t => t.GetTenantByID(DriveTestSupport.ProjectKey)).Returns(new Tenant
        {
            TenantId = DriveTestSupport.ProjectKey,
            DbConnectionString = string.Empty,
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
            Applications = [new Applications { Domain = "sign.example.com", IsDomainVerified = true }],
        });

        _repository.Setup(r => r.GetConnectionAsync(DriveTestSupport.ProjectKey, DriveTestSupport.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _stored);
        _repository.Setup(r => r.UpsertConnectionAsync(It.IsAny<DriveConnection>(), It.IsAny<CancellationToken>()))
            .Callback<DriveConnection, CancellationToken>((c, _) => _stored = c)
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.DeleteConnectionAsync(DriveTestSupport.ProjectKey, DriveTestSupport.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { var had = _stored is not null; _stored = null; return had; });
        _repository.Setup(r => r.InsertStateAsync(It.IsAny<DriveOAuthState>(), It.IsAny<CancellationToken>()))
            .Callback<DriveOAuthState, CancellationToken>((s, _) => _state = s)
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.TakeStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                var found = _state?.ItemId == id ? _state : null;
                _state = null;
                return found;
            });

        var options = DriveTestSupport.Options();
        var secrets = DriveTestSupport.Secrets();
        _service = new DriveConnectionService(
            _repository.Object,
            new EntraOAuthClient(DriveTestSupport.Factory(_http), options, secrets, NullLogger<EntraOAuthClient>.Instance),
            new DriveTokenProtector(secrets),
            _tenants.Object,
            options,
            _clock,
            _metrics,
            new StartConnectionRequestValidator(),
            NullLogger<DriveConnectionService>.Instance);
    }

    public void Dispose()
    {
        DriveTestSupport.ClearCaller();
        _metrics.Dispose();
    }

    [Fact]
    public async Task AC1_start_returns_an_authorize_url_and_stores_a_single_use_state_with_a_ttl()
    {
        var result = await _service.StartAsync(new StartConnectionRequest { ReturnUrl = "https://sign.example.com/drive/done" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Connected.Should().BeFalse();
        result.Value.AuthorizeUrl.Should().Contain("code_challenge_method=S256");
        result.Value.AuthorizeUrl.Should().Contain($"state={_state!.ItemId}");
        _state.CodeVerifier.Should().NotBeNullOrEmpty();
        _state.ReturnUrl.Should().Be("https://sign.example.com/drive/done");
        _state.ExpiresAt.Should().Be(_clock.GetUtcNow().UtcDateTime.AddMinutes(10),
            because: "an abandoned consent screen must not leave a state that works forever");
    }

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("http://sign.example.com/insecure")]
    [InlineData("/relative")]
    public async Task AC2_a_return_url_off_the_project_domains_is_refused_and_nothing_is_stored(string returnUrl)
    {
        var result = await _service.StartAsync(new StartConnectionRequest { ReturnUrl = returnUrl }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(DriveFailureKind.InvalidRequest, because: "the callback would otherwise be an open redirect");
        _state.Should().BeNull();
    }

    [Fact]
    public async Task AC3_the_callback_stores_an_encrypted_connection_consumes_the_state_and_redirects_connected()
    {
        _http.On(HttpMethod.Post, "/oauth2/v2.0/token", HttpStatusCode.OK, DriveTestSupport.TokenJson());
        await _service.StartAsync(new StartConnectionRequest { ReturnUrl = "https://sign.example.com/done?step=2" }, CancellationToken.None);
        var stateId = _state!.ItemId;
        var verifier = _state.CodeVerifier;

        var outcome = await _service.CompleteAsync("code-1", stateId, null, null, CancellationToken.None);

        outcome.Status.Should().Be(ConnectionCallbackOutcome.Connected);
        outcome.RedirectUrl.Should().Be("https://sign.example.com/done?step=2&driveStatus=connected");
        _stored.Should().NotBeNull();
        _stored!.RefreshTokenCiphertext.Should().NotContain("refresh-1", because: "the refresh token is stored encrypted");
        _stored.AccountDisplayName.Should().Be("Ada Lovelace");
        _stored.MicrosoftTenantId.Should().Be("tenant-abc");
        _state.Should().BeNull(because: "the state is single use");
        _http.Requests.Single().Body.Should().Contain("code_verifier=" + verifier,
            because: "PKCE binds the code to the verifier stored with the state");
    }

    [Fact]
    public async Task AC4_a_replayed_callback_is_refused_and_the_first_connection_is_untouched()
    {
        _http.On(HttpMethod.Post, "/oauth2/v2.0/token", HttpStatusCode.OK, DriveTestSupport.TokenJson());
        await _service.StartAsync(new StartConnectionRequest { ReturnUrl = "https://sign.example.com/done" }, CancellationToken.None);
        var stateId = _state!.ItemId;
        await _service.CompleteAsync("code-1", stateId, null, null, CancellationToken.None);
        var first = _stored;

        var replay = await _service.CompleteAsync("code-2", stateId, null, null, CancellationToken.None);

        replay.RedirectUrl.Should().BeNull(because: "there is no trusted return URL for an unknown state");
        _stored.Should().BeSameAs(first);
        _http.Requests.Should().HaveCount(1, because: "no second code exchange may happen");
    }

    [Fact]
    public async Task An_expired_state_is_refused_even_if_mongo_has_not_evicted_it_yet()
    {
        await _service.StartAsync(new StartConnectionRequest { ReturnUrl = "https://sign.example.com/done" }, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(11));

        var outcome = await _service.CompleteAsync("code-1", _state!.ItemId, null, null, CancellationToken.None);

        outcome.RedirectUrl.Should().BeNull();
    }

    [Fact]
    public async Task AC5_admin_consent_required_is_redirected_as_such_and_status_offers_the_admin_url()
    {
        await _service.StartAsync(new StartConnectionRequest { ReturnUrl = "https://sign.example.com/done" }, CancellationToken.None);

        var outcome = await _service.CompleteAsync(null, _state!.ItemId, "access_denied", "AADSTS65001: admin consent needed", CancellationToken.None);
        var status = await _service.GetStatusAsync(CancellationToken.None);

        outcome.Status.Should().Be(ConnectionCallbackOutcome.AdminConsentRequired);
        outcome.RedirectUrl.Should().Be("https://sign.example.com/done?driveStatus=admin_consent_required");
        status.Value!.Connected.Should().BeFalse();
        status.Value.AdminConsentUrl.Should().Contain("/organizations/v2.0/adminconsent");
    }

    [Fact]
    public async Task A_refused_consent_redirects_failed_with_the_reason()
    {
        await _service.StartAsync(new StartConnectionRequest { ReturnUrl = "https://sign.example.com/done" }, CancellationToken.None);

        var outcome = await _service.CompleteAsync(null, _state!.ItemId, "access_denied", "user said no", CancellationToken.None);

        outcome.RedirectUrl.Should().Be("https://sign.example.com/done?driveStatus=failed&reason=access_denied");
    }

    [Fact]
    public async Task AC6_AC7_status_reflects_the_connection_and_disconnect_removes_it()
    {
        await ConnectAsync();

        var before = await _service.GetStatusAsync(CancellationToken.None);
        var disconnect = await _service.DisconnectAsync(CancellationToken.None);
        var after = await _service.GetStatusAsync(CancellationToken.None);
        var token = await _service.GetAccessTokenAsync(CancellationToken.None);

        before.Value!.Connected.Should().BeTrue();
        before.Value.AccountDisplayName.Should().Be("Ada Lovelace");
        before.Value.ConnectedAt.Should().Be(_clock.GetUtcNow().UtcDateTime);
        disconnect.Value.Should().BeTrue();
        after.Value!.Connected.Should().BeFalse();
        token.FailureKind.Should().Be(DriveFailureKind.NotConnected,
            because: "the cached access token must die with the connection, not outlive it");
    }

    [Fact]
    public async Task AC8_a_rejected_refresh_token_deletes_the_connection_and_reports_not_connected()
    {
        await ConnectAsync();
        _clock.Advance(TimeSpan.FromHours(2));
        _http.On(HttpMethod.Post, "/oauth2/v2.0/token", HttpStatusCode.BadRequest,
            "{\"error\":\"invalid_grant\",\"error_description\":\"AADSTS70000: expired\"}", once: true);

        var token = await _service.GetAccessTokenAsync(CancellationToken.None);

        token.FailureKind.Should().Be(DriveFailureKind.NotConnected);
        _stored.Should().BeNull(because: "a dead grant can never be refreshed; the user must reconnect");
    }

    [Fact]
    public async Task AC9_starting_again_while_connected_says_connected_and_mints_no_url()
    {
        await ConnectAsync();
        _state = null;

        var result = await _service.StartAsync(new StartConnectionRequest { ReturnUrl = "https://sign.example.com/done" }, CancellationToken.None);

        result.Value!.Connected.Should().BeTrue();
        result.Value.AuthorizeUrl.Should().BeNull();
        _state.Should().BeNull();
    }

    [Fact]
    public async Task The_access_token_is_cached_until_near_expiry_then_refreshed_once_and_the_rotated_refresh_token_is_stored()
    {
        await ConnectAsync();
        _http.On(HttpMethod.Post, "/oauth2/v2.0/token", HttpStatusCode.OK, DriveTestSupport.TokenJson(accessToken: "access-2", refreshToken: "refresh-2"));
        var protector = new DriveTokenProtector(DriveTestSupport.Secrets());

        var cached = await _service.GetAccessTokenAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(59));
        var refreshed = await _service.GetAccessTokenAsync(CancellationToken.None);
        var again = await _service.GetAccessTokenAsync(CancellationToken.None);

        cached.Value.Should().Be("access-1", because: "a token with 59 minutes left is reused");
        refreshed.Value.Should().Be("access-2", because: "inside the 60 second skew the token is refreshed");
        again.Value.Should().Be("access-2");
        _http.Requests.Count(r => r.Body!.Contains("grant_type=refresh_token", StringComparison.Ordinal)).Should().Be(1);
        (await protector.UnprotectAsync(_stored!.RefreshTokenCiphertext, CancellationToken.None)).Should().Be("refresh-2",
            because: "personal accounts rotate refresh tokens; the old one is dead after this call");
    }

    [Fact]
    public async Task Parallel_callers_share_one_refresh()
    {
        await ConnectAsync();
        _clock.Advance(TimeSpan.FromHours(2));
        _http.On(HttpMethod.Post, "/oauth2/v2.0/token", HttpStatusCode.OK, DriveTestSupport.TokenJson(accessToken: "access-2"));

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _service.GetAccessTokenAsync(CancellationToken.None)));

        results.Should().AllSatisfy(r => r.Value.Should().Be("access-2"));
        _http.Requests.Count(r => r.Body!.Contains("grant_type=refresh_token", StringComparison.Ordinal)).Should().Be(1,
            because: "ten parallel requests from one user must not cost ten calls to Entra");
    }

    [Fact]
    public async Task AC31_without_entra_configuration_start_is_refused_as_not_configured()
    {
        var options = DriveTestSupport.Options(o => o.ClientId = null);
        var service = new DriveConnectionService(
            _repository.Object,
            new EntraOAuthClient(DriveTestSupport.Factory(_http), options, DriveTestSupport.Secrets(), NullLogger<EntraOAuthClient>.Instance),
            new DriveTokenProtector(DriveTestSupport.Secrets()),
            _tenants.Object,
            options,
            _clock,
            _metrics,
            new StartConnectionRequestValidator(),
            NullLogger<DriveConnectionService>.Instance);

        var result = await service.StartAsync(new StartConnectionRequest { ReturnUrl = "https://sign.example.com/done" }, CancellationToken.None);

        result.FailureKind.Should().Be(DriveFailureKind.NotConfigured);
    }

    [Fact]
    public async Task Without_the_encryption_key_in_blocks_secrets_start_is_refused_as_not_configured()
    {
        var options = DriveTestSupport.Options();
        var secrets = DriveTestSupport.Secrets(key: null);
        var service = new DriveConnectionService(
            _repository.Object,
            new EntraOAuthClient(DriveTestSupport.Factory(_http), options, secrets, NullLogger<EntraOAuthClient>.Instance),
            new DriveTokenProtector(secrets),
            _tenants.Object,
            options,
            _clock,
            _metrics,
            new StartConnectionRequestValidator(),
            NullLogger<DriveConnectionService>.Instance);

        var result = await service.StartAsync(new StartConnectionRequest { ReturnUrl = "https://sign.example.com/done" }, CancellationToken.None);

        result.FailureKind.Should().Be(DriveFailureKind.NotConfigured,
            because: "a connection whose refresh token could not be encrypted must never be created");
    }

    [Fact]
    public async Task The_callback_runs_as_the_project_that_started_the_flow()
    {
        _http.On(HttpMethod.Post, "/oauth2/v2.0/token", HttpStatusCode.OK, DriveTestSupport.TokenJson());
        await _service.StartAsync(new StartConnectionRequest { ReturnUrl = "https://sign.example.com/done" }, CancellationToken.None);
        DriveTestSupport.ClearCaller();

        var outcome = await _service.CompleteAsync("code-1", _state!.ItemId, null, null, CancellationToken.None);

        outcome.Status.Should().Be(ConnectionCallbackOutcome.Connected,
            because: "Entra sends no tenant header; the project comes from the stored state");
        _stored!.ProjectKey.Should().Be(DriveTestSupport.ProjectKey);
        BlocksContext.GetContext().Should().BeNull(because: "the borrowed context must not leak past the callback");
    }

    [Fact]
    public async Task AC30_the_caller_identity_comes_from_the_context_only()
    {
        DriveTestSupport.SetCaller(userId: "someone-else");

        var status = await _service.GetStatusAsync(CancellationToken.None);

        status.IsSuccess.Should().BeTrue();
        _repository.Verify(r => r.GetConnectionAsync(DriveTestSupport.ProjectKey, "someone-else", It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.GetConnectionAsync(DriveTestSupport.ProjectKey, DriveTestSupport.UserId, It.IsAny<CancellationToken>()), Times.Never);
    }

    private async Task ConnectAsync()
    {
        _http.On(HttpMethod.Post, "/oauth2/v2.0/token", HttpStatusCode.OK, DriveTestSupport.TokenJson(), once: true);
        await _service.StartAsync(new StartConnectionRequest { ReturnUrl = "https://sign.example.com/done" }, CancellationToken.None);
        var outcome = await _service.CompleteAsync("code-1", _state!.ItemId, null, null, CancellationToken.None);
        outcome.Status.Should().Be(ConnectionCallbackOutcome.Connected);
    }
}
