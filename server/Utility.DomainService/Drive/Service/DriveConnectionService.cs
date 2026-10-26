using System.Collections.Concurrent;
using System.Security.Cryptography;
using Blocks.Genesis;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Utility.DomainService.Drive.Models;

namespace Utility.DomainService.Drive.Service;

public interface IDriveConnectionService
{
    Task<DriveResult<StartConnectionResponse>> StartAsync(StartConnectionRequest request, CancellationToken cancellationToken);

    Task<ConnectionCallbackOutcome> CompleteAsync(
        string? code, string? state, string? error, string? errorDescription, CancellationToken cancellationToken);

    Task<DriveResult<ConnectionStatusResponse>> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>False when there was nothing to disconnect.</summary>
    Task<DriveResult<bool>> DisconnectAsync(CancellationToken cancellationToken);

    /// <summary>A live Graph access token for the calling user, refreshing if needed.</summary>
    Task<DriveResult<string>> GetAccessTokenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The connect / callback / status / disconnect lifecycle and the access-token cache.
/// </summary>
/// <remarks>
/// Who the caller is comes from <see cref="BlocksContext"/> only: the project is the
/// <c>x-blocks-key</c> tenant and the user is the bearer token's subject. Nothing in a request
/// body can point at another user's connection.
/// <para>
/// Access tokens are cached in memory per connection and refreshed under a per-connection lock,
/// so a burst of parallel requests from one user costs one call to Entra, not one each. Entra
/// rotates refresh tokens for personal accounts on every refresh, so the token that comes back is
/// always persisted; forgetting that would kill every personal-account connection after an hour.
/// </para>
/// </remarks>
public sealed class DriveConnectionService : IDriveConnectionService
{
    private const string QueryStatus = "driveStatus";
    private const int RefreshSkewSeconds = 60;

    private readonly IDriveRepository _repository;
    private readonly IEntraOAuthClient _entra;
    private readonly DriveTokenProtector _protector;
    private readonly ITenants _tenants;
    private readonly IOptions<DriveOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly DriveMetrics _metrics;
    private readonly IValidator<StartConnectionRequest> _startValidator;
    private readonly ILogger<DriveConnectionService> _logger;

    private readonly ConcurrentDictionary<string, CachedToken> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public DriveConnectionService(
        IDriveRepository repository,
        IEntraOAuthClient entra,
        DriveTokenProtector protector,
        ITenants tenants,
        IOptions<DriveOptions> options,
        TimeProvider timeProvider,
        DriveMetrics metrics,
        IValidator<StartConnectionRequest> startValidator,
        ILogger<DriveConnectionService> logger)
    {
        _repository = repository;
        _entra = entra;
        _protector = protector;
        _tenants = tenants;
        _options = options;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _startValidator = startValidator;
        _logger = logger;
    }

    public async Task<DriveResult<StartConnectionResponse>> StartAsync(StartConnectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _startValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return DriveResult<StartConnectionResponse>.Fail(DriveFailureKind.InvalidRequest, validation.ToString("; "));
        }

        if (!TryGetCaller(out var projectKey, out var userId))
        {
            return DriveResult<StartConnectionResponse>.Fail(DriveFailureKind.InvalidRequest, "No user in the request context.");
        }

        if (!IsReturnUrlAllowed(request.ReturnUrl!, projectKey))
        {
            return DriveResult<StartConnectionResponse>.Fail(DriveFailureKind.InvalidRequest, "returnUrl is not on one of the project's domains.");
        }

        if (!_entra.IsConfigured || !await _protector.IsConfiguredAsync(cancellationToken))
        {
            _logger.LogWarning("Drive connect refused: Entra client or token encryption key is not configured");
            return DriveResult<StartConnectionResponse>.Fail(DriveFailureKind.NotConfigured, "The drive integration is not configured.");
        }

        var existing = await _repository.GetConnectionAsync(projectKey, userId, cancellationToken);
        if (existing is not null)
        {
            return DriveResult<StartConnectionResponse>.Ok(new StartConnectionResponse { Connected = true });
        }

        var stateId = Base64Url(RandomNumberGenerator.GetBytes(32));
        var (authorizeUrl, verifier) = _entra.BuildAuthorizeUrl(stateId);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        await _repository.InsertStateAsync(
            new DriveOAuthState
            {
                ItemId = stateId,
                ProjectKey = projectKey,
                UserId = userId,
                ReturnUrl = request.ReturnUrl!,
                CodeVerifier = verifier,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(_options.Value.StateTtlMinutes),
            },
            cancellationToken);

        return DriveResult<StartConnectionResponse>.Ok(new StartConnectionResponse { AuthorizeUrl = authorizeUrl });
    }

    public async Task<ConnectionCallbackOutcome> CompleteAsync(
        string? code, string? state, string? error, string? errorDescription, CancellationToken cancellationToken)
    {
        var failed = new ConnectionCallbackOutcome { Status = ConnectionCallbackOutcome.Failed };
        if (string.IsNullOrWhiteSpace(state))
        {
            return failed;
        }

        var stored = await _repository.TakeStateAsync(state, cancellationToken);
        if (stored is null || stored.ExpiresAt < _timeProvider.GetUtcNow().UtcDateTime)
        {
            _logger.LogWarning("Drive callback with unknown or expired state");
            return failed;
        }

        // Entra calls back with no tenant header, but the secrets this exchange needs belong to
        // the project that started it. The state says which; run the rest as that project.
        var previous = BlocksContext.GetContext();
        BlocksContext.SetContext(BlocksContext.Create(
            stored.ProjectKey, null, stored.UserId, true, null, null,
            DateTime.UtcNow.AddMinutes(5), null, null, null, null, null, null, null));
        try
        {
            return await CompleteInContextAsync(code, stored, error, errorDescription, cancellationToken);
        }
        finally
        {
            BlocksContext.SetContext(previous);
        }
    }

    private async Task<ConnectionCallbackOutcome> CompleteInContextAsync(
        string? code, DriveOAuthState stored, string? error, string? errorDescription, CancellationToken cancellationToken)
    {

        if (!string.IsNullOrEmpty(error))
        {
            return EntraOAuthClient.IsAdminConsentError(error, errorDescription)
                ? Redirect(stored.ReturnUrl, ConnectionCallbackOutcome.AdminConsentRequired)
                : Redirect(stored.ReturnUrl, ConnectionCallbackOutcome.Failed, KnownReason(error));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Redirect(stored.ReturnUrl, ConnectionCallbackOutcome.Failed, "missing_code");
        }

        var token = await _entra.ExchangeCodeAsync(code, stored.CodeVerifier, cancellationToken);
        if (token.AdminConsentRequired)
        {
            return Redirect(stored.ReturnUrl, ConnectionCallbackOutcome.AdminConsentRequired);
        }

        if (!token.IsSuccess)
        {
            return Redirect(stored.ReturnUrl, ConnectionCallbackOutcome.Failed, KnownReason(token.Error));
        }

        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            // offline_access was not granted; without a refresh token the connection would die in an hour.
            return Redirect(stored.ReturnUrl, ConnectionCallbackOutcome.Failed, "no_refresh_token");
        }

        var ciphertext = await _protector.ProtectAsync(token.RefreshToken, cancellationToken);
        if (ciphertext is null)
        {
            return Redirect(stored.ReturnUrl, ConnectionCallbackOutcome.Failed, "not_configured");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _repository.UpsertConnectionAsync(
            new DriveConnection
            {
                ItemId = Guid.NewGuid().ToString(),
                ProjectKey = stored.ProjectKey,
                UserId = stored.UserId,
                RefreshTokenCiphertext = ciphertext,
                EncryptionKeyId = _options.Value.TokenEncryptionKeyId,
                MicrosoftTenantId = token.MicrosoftTenantId,
                AccountDisplayName = token.DisplayName,
                AccountEmail = token.Email,
                Scopes = token.Scope,
                ConnectedAt = now,
                LastUsedAt = now,
            },
            cancellationToken);

        Cache(stored.ProjectKey, stored.UserId, token);
        _metrics.ConnectionCreated();
        _logger.LogInformation("Drive connected for user in project {ProjectKey}", stored.ProjectKey);

        return Redirect(stored.ReturnUrl, ConnectionCallbackOutcome.Connected);
    }

    public async Task<DriveResult<ConnectionStatusResponse>> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var projectKey, out var userId))
        {
            return DriveResult<ConnectionStatusResponse>.Fail(DriveFailureKind.InvalidRequest, "No user in the request context.");
        }

        var connection = await _repository.GetConnectionAsync(projectKey, userId, cancellationToken);
        return DriveResult<ConnectionStatusResponse>.Ok(new ConnectionStatusResponse
        {
            Connected = connection is not null,
            AccountDisplayName = connection?.AccountDisplayName,
            AccountEmail = connection?.AccountEmail,
            ConnectedAt = connection?.ConnectedAt,
            AdminConsentUrl = _entra.IsConfigured ? _entra.BuildAdminConsentUrl() : null,
        });
    }

    public async Task<DriveResult<bool>> DisconnectAsync(CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var projectKey, out var userId))
        {
            return DriveResult<bool>.Fail(DriveFailureKind.InvalidRequest, "No user in the request context.");
        }

        var deleted = await _repository.DeleteConnectionAsync(projectKey, userId, cancellationToken);
        _tokens.TryRemove(CacheKey(projectKey, userId), out _);
        if (deleted)
        {
            _metrics.ConnectionRevoked("user");
        }

        return DriveResult<bool>.Ok(deleted);
    }

    public async Task<DriveResult<string>> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!TryGetCaller(out var projectKey, out var userId))
        {
            return DriveResult<string>.Fail(DriveFailureKind.InvalidRequest, "No user in the request context.");
        }

        var key = CacheKey(projectKey, userId);
        if (TryGetFresh(key, out var cached))
        {
            return DriveResult<string>.Ok(cached);
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGetFresh(key, out cached))
            {
                return DriveResult<string>.Ok(cached);
            }

            var connection = await _repository.GetConnectionAsync(projectKey, userId, cancellationToken);
            if (connection is null)
            {
                return NotConnected();
            }

            var refreshToken = await _protector.UnprotectAsync(connection.RefreshTokenCiphertext, cancellationToken);
            if (refreshToken is null)
            {
                _logger.LogError("Stored drive refresh token could not be decrypted; dropping connection");
                await RevokeAsync(projectKey, userId, "undecryptable", cancellationToken);
                return NotConnected();
            }

            var token = await _entra.RefreshAsync(refreshToken, cancellationToken);
            if (token.AdminConsentRequired)
            {
                return DriveResult<string>.Fail(DriveFailureKind.AdminConsentRequired, "An administrator must approve the app for this organization.");
            }

            if (token.GrantInvalid)
            {
                await RevokeAsync(projectKey, userId, "invalid_grant", cancellationToken);
                return NotConnected();
            }

            if (!token.IsSuccess)
            {
                return DriveResult<string>.Fail(DriveFailureKind.ProviderError, "Microsoft did not issue an access token.");
            }

            Cache(projectKey, userId, token);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (!string.IsNullOrEmpty(token.RefreshToken) && token.RefreshToken != refreshToken)
            {
                var rotated = await _protector.ProtectAsync(token.RefreshToken, cancellationToken);
                if (rotated is not null)
                {
                    connection.RefreshTokenCiphertext = rotated;
                    connection.EncryptionKeyId = _options.Value.TokenEncryptionKeyId;
                    connection.LastUsedAt = now;
                    await _repository.UpsertConnectionAsync(connection, cancellationToken);
                    return DriveResult<string>.Ok(token.AccessToken!);
                }
            }

            await _repository.TouchConnectionAsync(projectKey, userId, now, cancellationToken);
            return DriveResult<string>.Ok(token.AccessToken!);
        }
        finally
        {
            gate.Release();
        }
    }

    private static DriveResult<string> NotConnected() =>
        DriveResult<string>.Fail(DriveFailureKind.NotConnected, "No Microsoft account is connected for this user.");

    private async Task RevokeAsync(string projectKey, string userId, string reason, CancellationToken cancellationToken)
    {
        await _repository.DeleteConnectionAsync(projectKey, userId, cancellationToken);
        _tokens.TryRemove(CacheKey(projectKey, userId), out _);
        _metrics.ConnectionRevoked(reason);
    }

    private bool TryGetFresh(string key, out string token)
    {
        token = string.Empty;
        if (_tokens.TryGetValue(key, out var cached) && cached.ExpiresAt > _timeProvider.GetUtcNow().AddSeconds(RefreshSkewSeconds))
        {
            token = cached.AccessToken;
            return true;
        }

        return false;
    }

    private void Cache(string projectKey, string userId, EntraTokenResult token) =>
        _tokens[CacheKey(projectKey, userId)] = new CachedToken(
            token.AccessToken!,
            _timeProvider.GetUtcNow().AddSeconds(token.ExpiresInSeconds));

    private static string CacheKey(string projectKey, string userId) => $"{projectKey}:{userId}";

    private static bool TryGetCaller(out string projectKey, out string userId)
    {
        var context = BlocksContext.GetContext();
        projectKey = context?.TenantId ?? string.Empty;
        userId = context?.UserId ?? string.Empty;
        return projectKey.Length > 0 && userId.Length > 0;
    }

    /// <summary>
    /// A return URL is followed only if it is absolute, https (or localhost in development), and
    /// its host is the project's application domain or one of its CORS origins. Anything else
    /// would make the callback an open redirect that carries a "connected" signal.
    /// </summary>
    private bool IsReturnUrlAllowed(string returnUrl, string projectKey)
    {
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var isLocalhost = uri.IsLoopback;
        if (isLocalhost)
        {
            return _options.Value.AllowLocalhostReturnUrl;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var tenant = _tenants.GetTenantByID(projectKey);
        if (tenant?.Applications is null)
        {
            return false;
        }

        // The project's registered application domains, as Genesis keeps them. Unverified ones
        // count too: a project in the middle of DNS setup should still be able to test the flow.
        foreach (var application in tenant.Applications)
        {
            if (HostMatches(uri.Host, application?.Domain))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HostMatches(string host, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var candidateHost = Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            ? uri.Host
            : candidate.Trim().TrimEnd('/');

        return string.Equals(host, candidateHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The reasons a caller can be told, as a closed set. Entra's <c>error</c> arrives on the
    /// query string of an anonymous endpoint, so it is never echoed into the redirect: anything
    /// outside the set becomes <c>provider_error</c>, and the real value stays in the log.
    /// </summary>
    private static readonly string[] KnownReasons =
        ["access_denied", "invalid_grant", "invalid_request", "invalid_client", "unauthorized_client",
         "server_error", "temporarily_unavailable", "not_configured", "invalid_response", "unreachable"];

    private static string KnownReason(string? error) =>
        error is not null && KnownReasons.Contains(error, StringComparer.Ordinal) ? error : "provider_error";

    private static ConnectionCallbackOutcome Redirect(string returnUrl, string status, string? reason = null)
    {
        var separator = returnUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var url = $"{returnUrl}{separator}{QueryStatus}={status}";
        if (reason is not null)
        {
            url += $"&reason={Uri.EscapeDataString(reason)}";
        }

        return new ConnectionCallbackOutcome { RedirectUrl = url, Status = status };
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);
}
