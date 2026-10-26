using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Utility.DomainService.Drive.Service;

namespace XUnitTest.Drive;

/// <summary>
/// Guards the two Entra calls: the authorize URL the user is sent to, and how the token
/// endpoint's answers are classified. A wrong classification turns "ask your admin" into a
/// silent reconnect loop, or a dead token into an endless 502.
/// </summary>
public sealed class EntraOAuthClientTests
{
    private readonly ScriptedHttpHandler _http = new();
    private readonly EntraOAuthClient _client;

    public EntraOAuthClientTests()
    {
        _client = new EntraOAuthClient(DriveTestSupport.Factory(_http), DriveTestSupport.Options(), NullLogger<EntraOAuthClient>.Instance);
    }

    [Fact]
    public void Authorize_url_carries_state_pkce_scopes_and_redirect()
    {
        var (url, verifier) = _client.BuildAuthorizeUrl("state-123");

        url.Should().StartWith("https://login.microsoftonline.com/common/oauth2/v2.0/authorize?");
        url.Should().Contain("client_id=client-id");
        url.Should().Contain("state=state-123");
        url.Should().Contain("code_challenge_method=S256");
        url.Should().Contain("code_challenge=");
        url.Should().Contain("scope=Files.ReadWrite.All%20Sites.ReadWrite.All%20offline_access");
        url.Should().Contain("redirect_uri=https%3A%2F%2Futilities.test%2Fapi%2Fdrive%2Fconnections%2Fcallback");
        verifier.Length.Should().BeGreaterThanOrEqualTo(43, because: "PKCE verifiers must be at least 43 characters");
    }

    [Fact]
    public void Admin_consent_url_targets_organizations_not_common()
    {
        _client.BuildAdminConsentUrl().Should().StartWith("https://login.microsoftonline.com/organizations/v2.0/adminconsent?",
            because: "an admin consents for one directory; 'common' cannot receive admin consent");
    }

    [Fact]
    public async Task Exchange_posts_the_code_with_the_verifier_and_reads_identity_claims()
    {
        _http.On(HttpMethod.Post, "/oauth2/v2.0/token", HttpStatusCode.OK, DriveTestSupport.TokenJson());

        var result = await _client.ExchangeCodeAsync("code-1", "verifier-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.AccessToken.Should().Be("access-1");
        result.RefreshToken.Should().Be("refresh-1");
        result.MicrosoftTenantId.Should().Be("tenant-abc");
        result.DisplayName.Should().Be("Ada Lovelace");
        result.Email.Should().Be("ada@example.com");

        var body = _http.Requests.Single().Body!;
        body.Should().Contain("grant_type=authorization_code");
        body.Should().Contain("code=code-1");
        body.Should().Contain("code_verifier=verifier-1");
        body.Should().Contain("client_secret=client-secret");
    }

    [Fact]
    public async Task AADSTS65001_is_reported_as_admin_consent_required_not_as_a_dead_grant()
    {
        _http.On(HttpMethod.Post, "/oauth2/v2.0/token", HttpStatusCode.BadRequest,
            "{\"error\":\"invalid_grant\",\"error_description\":\"AADSTS65001: The user or administrator has not consented\"}");

        var result = await _client.RefreshAsync("refresh-1", CancellationToken.None);

        result.AdminConsentRequired.Should().BeTrue();
        result.GrantInvalid.Should().BeFalse(because: "deleting the connection would hide the real fix, which is admin consent");
    }

    [Fact]
    public async Task Invalid_grant_without_consent_text_is_a_dead_grant()
    {
        _http.On(HttpMethod.Post, "/oauth2/v2.0/token", HttpStatusCode.BadRequest,
            "{\"error\":\"invalid_grant\",\"error_description\":\"AADSTS70000: The refresh token has expired\"}");

        var result = await _client.RefreshAsync("refresh-1", CancellationToken.None);

        result.GrantInvalid.Should().BeTrue(because: "the user must reconnect; retrying would never succeed");
        result.AdminConsentRequired.Should().BeFalse();
    }

    [Fact]
    public async Task A_non_json_answer_is_a_failure_not_an_exception()
    {
        _http.On(HttpMethod.Post, "/oauth2/v2.0/token", _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>") });

        var result = await _client.RefreshAsync("refresh-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("invalid_response");
    }
}
