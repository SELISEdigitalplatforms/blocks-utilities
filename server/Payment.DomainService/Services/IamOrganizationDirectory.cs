using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Models;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Resolves organizations against IAM, the directory of record.
/// </summary>
/// <remarks>
/// The caller's own bearer token is forwarded rather than a service credential. IAM then
/// scopes the lookup to that token's tenant and enforces its own read permission, so this
/// service neither reimplements tenant isolation nor holds an authority the caller lacks.
/// A caller who cannot read an organization in IAM cannot register a provider under it.
/// </remarks>
public sealed class IamOrganizationDirectory : IOrganizationDirectory
{
    private const string OrganizationsPath = "api/iam/organizations";
    private const string BearerPrefix = "Bearer ";

    private readonly IHttpService _httpService;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<IamOrganizationDirectory> _logger;

    public IamOrganizationDirectory(
        IHttpService httpService,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<IamOrganizationDirectory> logger)
    {
        _httpService = httpService;
        _options = options;
        _logger = logger;
    }

    public async Task<OrganizationLookupOutcome> FindAsync(
        string organizationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return OrganizationLookupOutcome.NotFound;
        }

        if (!TryBuildUrl(organizationId, out var url))
        {
            // Unconfigured is an operator error, not a caller error, so it must not read as
            // "that organization does not exist" and send someone hunting in IAM.
            _logger.LogError(
                "Organization verification is unavailable Reason=iam_base_url_not_configured");

            return OrganizationLookupOutcome.Unavailable;
        }

        var token = BlocksContext.GetContext()?.OAuthToken;

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogError(
                "Organization verification is unavailable Reason=caller_token_unavailable");

            return OrganizationLookupOutcome.Unavailable;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = token.StartsWith(
                BearerPrefix,
                StringComparison.OrdinalIgnoreCase)
                ? token
                : BearerPrefix + token
        };

        try
        {
            var (response, error) =
                await _httpService.SendRequest<IamOrganizationResponse>(
                    HttpMethod.Get,
                    url,
                    null!,
                    "application/json",
                    headers,
                    cancellationToken,
                    Math.Clamp(
                        _options.CurrentValue.ProviderTimeoutSeconds,
                        1,
                        60));

            if (response?.Organization != null &&
                !string.IsNullOrWhiteSpace(response.Organization.ItemId))
            {
                return OrganizationLookupOutcome.Found;
            }

            // A well-formed reply carrying no organization is IAM saying it has no such
            // record. Anything else — a transport error, an empty body, a rejected token —
            // leaves us unable to tell, and that is not the same answer.
            if (response != null && string.IsNullOrWhiteSpace(error))
            {
                return OrganizationLookupOutcome.NotFound;
            }

            _logger.LogWarning(
                "Organization verification did not return a usable response OrganizationHash={OrganizationHash} HasError={HasError}",
                PaymentLogValue.Hash(organizationId),
                !string.IsNullOrWhiteSpace(error));

            return OrganizationLookupOutcome.Unavailable;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Organization verification timed out OrganizationHash={OrganizationHash}",
                PaymentLogValue.Hash(organizationId));

            return OrganizationLookupOutcome.Unavailable;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Organization verification failed OrganizationHash={OrganizationHash}",
                PaymentLogValue.Hash(organizationId));

            return OrganizationLookupOutcome.Unavailable;
        }
    }

    private bool TryBuildUrl(string organizationId, out string url)
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
            baseUri.AbsoluteUri.EndsWith('/')
                ? baseUri.AbsoluteUri
                : baseUri.AbsoluteUri + "/");

        url = new Uri(
            root,
            $"{OrganizationsPath}/{Uri.EscapeDataString(organizationId)}").AbsoluteUri;

        return true;
    }
}
