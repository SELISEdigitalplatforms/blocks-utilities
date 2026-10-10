using System.Text.Json.Serialization;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Reads members and organizations from IAM, on the caller's own token.
/// </summary>
/// <remarks>
/// The same model as <c>IamOrganizationDirectory</c>: the administrator's bearer token is
/// forwarded rather than a service credential, so IAM scopes each lookup to their tenant and
/// organization and enforces its own read permission. A caller IAM will not answer for gets a
/// seated member who is simply not mailed.
/// <para>
/// The user lookup deliberately sends no <c>organizationId</c> query parameter. IAM scopes by that
/// value when it is present instead of the token's organization, which would widen the read.
/// </para>
/// </remarks>
public sealed class IamMemberDirectory : IMemberDirectory
{
    private const string UsersPath = "api/iam/users";
    private const string OrganizationsPath = "api/iam/organizations";
    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Kept short: this runs inside an administrator's request, once per person named, and a slow
    /// IAM may delay the answer but must not hold it for the length of a payment call.
    /// </summary>
    private const int MaximumTimeoutSeconds = 5;

    private readonly IHttpService _httpService;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<IamMemberDirectory> _logger;

    public IamMemberDirectory(
        IHttpService httpService,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<IamMemberDirectory> logger)
    {
        _httpService = httpService;
        _options = options;
        _logger = logger;
    }

    public async Task<MemberContact?> FindUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var reply = await GetAsync<IamUserReply>(UsersPath, userId, cancellationToken);

        // An empty "data" is how IAM says the person is not in the caller's organization.
        if (reply?.Data is not { Email: { Length: > 0 } email } user || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var name = $"{user.FirstName} {user.LastName}".Trim();

        return new MemberContact(
            email.Trim(),
            name.Length > 0 ? name : email.Trim(),
            string.IsNullOrWhiteSpace(user.Language) ? null : user.Language.Trim());
    }

    public async Task<string?> FindOrganizationNameAsync(
        string organizationId,
        CancellationToken cancellationToken)
    {
        var reply = await GetAsync<IamOrganizationReply>(
            OrganizationsPath, organizationId, cancellationToken);

        return string.IsNullOrWhiteSpace(reply?.Organization?.Name)
            ? null
            : reply.Organization.Name.Trim();
    }

    private async Task<T?> GetAsync<T>(
        string path,
        string id,
        CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(id) || !TryBuildUrl(path, id, out var url))
        {
            return null;
        }

        var token = BlocksContext.GetContext()?.OAuthToken;

        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = token.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? token
                : BearerPrefix + token
        };

        try
        {
            var (reply, error) = await _httpService.SendRequest<T>(
                HttpMethod.Get,
                url,
                null!,
                "application/json",
                headers,
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, MaximumTimeoutSeconds));

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning(
                    "IAM lookup for a member email failed Path={Path} IdHash={IdHash}",
                    path,
                    PaymentLogValue.Hash(id));

                return null;
            }

            return reply;
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            // A timeout or transport failure costs this person their email, never their seat.
            _logger.LogWarning(
                exception,
                "IAM lookup for a member email did not complete Path={Path} IdHash={IdHash}",
                path,
                PaymentLogValue.Hash(id));

            return null;
        }
    }

    private bool TryBuildUrl(string path, string id, out string url)
    {
        url = string.Empty;
        var configured = _options.CurrentValue.IamBaseUrl;

        if (string.IsNullOrWhiteSpace(configured) ||
            !Uri.TryCreate(configured, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var root = new Uri(
            baseUri.AbsoluteUri.EndsWith('/') ? baseUri.AbsoluteUri : baseUri.AbsoluteUri + "/");

        url = new Uri(root, $"{path}/{Uri.EscapeDataString(id)}").AbsoluteUri;

        return true;
    }

    private sealed class IamUserReply
    {
        [JsonPropertyName("data")]
        public IamUser? Data { get; set; }
    }

    private sealed class IamUser
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }
    }

    private sealed class IamOrganizationReply
    {
        [JsonPropertyName("organization")]
        public IamOrganization? Organization { get; set; }
    }

    private sealed class IamOrganization
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
