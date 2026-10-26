using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Utility.DomainService.Drive.Service;

/// <summary>What the token endpoint handed back, already classified.</summary>
public sealed class EntraTokenResult
{
    public bool IsSuccess => AccessToken is not null;

    public string? AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    public int ExpiresInSeconds { get; init; }

    public string? Scope { get; init; }

    public string? MicrosoftTenantId { get; init; }

    public string? DisplayName { get; init; }

    public string? Email { get; init; }

    /// <summary>Entra's <c>error</c> value, e.g. <c>invalid_grant</c>.</summary>
    public string? Error { get; init; }

    public string? ErrorDescription { get; init; }

    /// <summary>AADSTS65001: the directory requires an admin to approve the app.</summary>
    public bool AdminConsentRequired { get; init; }

    /// <summary>The refresh token is dead (revoked, expired, password changed). Reconnect needed.</summary>
    public bool GrantInvalid { get; init; }
}

public interface IEntraOAuthClient
{
    /// <summary>
    /// The Entra authorize URL for this state. The PKCE verifier is returned beside it because
    /// the caller has to persist it with the state.
    /// </summary>
    (string AuthorizeUrl, string CodeVerifier) BuildAuthorizeUrl(string state);

    string BuildAdminConsentUrl();

    Task<EntraTokenResult> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken);

    Task<EntraTokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    bool IsConfigured { get; }
}

/// <summary>
/// The authorization-code and refresh-token calls against Entra, over plain HTTP.
/// </summary>
/// <remarks>
/// No MSAL: its token cache and account model fight with storing one refresh token per user per
/// project in our own database, and the two endpoints involved are a form post each. Identity
/// claims (directory id, display name, email) are read from the <c>id_token</c> payload without
/// signature validation, which is safe here only because the token arrived straight from the token
/// endpoint over TLS; it must never be done for a token a client supplied.
/// </remarks>
public sealed class EntraOAuthClient : IEntraOAuthClient
{
    public const string HttpClientName = "utility-drive-entra";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<DriveOptions> _options;
    private readonly IDriveSecrets _secrets;
    private readonly ILogger<EntraOAuthClient> _logger;

    public EntraOAuthClient(
        IHttpClientFactory httpClientFactory,
        IOptions<DriveOptions> options,
        IDriveSecrets secrets,
        ILogger<EntraOAuthClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _secrets = secrets;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.Value.ClientId)
        && !string.IsNullOrWhiteSpace(_options.Value.ClientSecretSecretId)
        && !string.IsNullOrWhiteSpace(_options.Value.RedirectUrl);

    public (string AuthorizeUrl, string CodeVerifier) BuildAuthorizeUrl(string state)
    {
        var options = _options.Value;
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var url = $"{options.AuthorityBaseUrl.TrimEnd('/')}/oauth2/v2.0/authorize"
            + $"?client_id={Uri.EscapeDataString(options.ClientId ?? string.Empty)}"
            + "&response_type=code"
            + $"&redirect_uri={Uri.EscapeDataString(options.RedirectUrl ?? string.Empty)}"
            + "&response_mode=query"
            + $"&scope={Uri.EscapeDataString(options.Scopes)}"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&code_challenge={challenge}"
            + "&code_challenge_method=S256"
            + "&prompt=select_account";

        return (url, verifier);
    }

    public string BuildAdminConsentUrl()
    {
        var options = _options.Value;
        return $"{options.AdminConsentBaseUrl.TrimEnd('/')}/v2.0/adminconsent"
            + $"?client_id={Uri.EscapeDataString(options.ClientId ?? string.Empty)}"
            + $"&scope={Uri.EscapeDataString(options.Scopes)}"
            + $"&redirect_uri={Uri.EscapeDataString(options.RedirectUrl ?? string.Empty)}";
    }

    public Task<EntraTokenResult> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken) =>
        PostTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["redirect_uri"] = _options.Value.RedirectUrl ?? string.Empty,
            },
            cancellationToken);

    public Task<EntraTokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        PostTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            },
            cancellationToken);

    private async Task<EntraTokenResult> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var clientSecret = await _secrets.GetClientSecretAsync(cancellationToken);
        if (string.IsNullOrEmpty(clientSecret))
        {
            _logger.LogWarning("Entra token request skipped: client secret not available from Blocks Secrets");
            return new EntraTokenResult { Error = "not_configured", ErrorDescription = "The Entra client secret is not provisioned for this project." };
        }

        form["client_id"] = options.ClientId ?? string.Empty;
        form["client_secret"] = clientSecret;
        form["scope"] = options.Scopes;

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.AuthorityBaseUrl.TrimEnd('/')}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(form)
        };

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Entra token endpoint unreachable");
            return new EntraTokenResult { Error = "unreachable", ErrorDescription = ex.Message };
        }

        using (response)
        {
            JsonDocument? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
            }
            catch (JsonException)
            {
                body = null;
            }

            using (body)
            {
                if (body is null)
                {
                    return new EntraTokenResult { Error = "invalid_response", ErrorDescription = $"HTTP {(int)response.StatusCode}" };
                }

                var root = body.RootElement;
                if (!response.IsSuccessStatusCode || root.TryGetProperty("error", out _))
                {
                    var error = root.TryGetProperty("error", out var e) ? e.GetString() : $"http_{(int)response.StatusCode}";
                    var description = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
                    var adminConsent = IsAdminConsentError(error, description);

                    // The description is Entra's own text; it quotes nothing we sent.
                    _logger.LogWarning("Entra token request failed: {Error} adminConsent={AdminConsent}", error, adminConsent);

                    return new EntraTokenResult
                    {
                        Error = error,
                        ErrorDescription = description,
                        AdminConsentRequired = adminConsent,
                        GrantInvalid = !adminConsent && error == "invalid_grant",
                    };
                }

                if (!root.TryGetProperty("access_token", out var access) || string.IsNullOrEmpty(access.GetString()))
                {
                    return new EntraTokenResult { Error = "invalid_response", ErrorDescription = "no access_token in a successful response" };
                }

                var claims = ReadIdTokenClaims(root.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null);

                return new EntraTokenResult
                {
                    AccessToken = access.GetString(),
                    RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
                    ExpiresInSeconds = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600,
                    Scope = root.TryGetProperty("scope", out var scope) ? scope.GetString() : null,
                    MicrosoftTenantId = claims.GetValueOrDefault("tid"),
                    DisplayName = claims.GetValueOrDefault("name"),
                    Email = claims.GetValueOrDefault("preferred_username") ?? claims.GetValueOrDefault("email"),
                };
            }
        }
    }

    /// <summary>
    /// Recognizes the "needs an admin" family of errors wherever Entra puts it: the callback's
    /// <c>error</c> query or the token endpoint's body.
    /// </summary>
    public static bool IsAdminConsentError(string? error, string? description) =>
        error is "consent_required" or "admin_consent_required"
        || (description is not null && description.Contains("AADSTS65001", StringComparison.Ordinal));

    private static Dictionary<string, string> ReadIdTokenClaims(string? idToken)
    {
        var claims = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(idToken))
        {
            return claims;
        }

        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            return claims;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));

            foreach (var property in json.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    claims[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            // Claims are a nicety for the status endpoint; a token without readable ones still works.
        }

        return claims;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
