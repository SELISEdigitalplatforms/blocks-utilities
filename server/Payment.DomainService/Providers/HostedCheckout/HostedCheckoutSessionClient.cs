using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.HostedCheckout;

public sealed class HostedCheckoutSessionClient : IPaymentSessionClient
{
    private readonly IHttpService _httpService;
    private readonly ICheckoutUrlPolicy _urlPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<HostedCheckoutSessionClient> _logger;

    public HostedCheckoutSessionClient(
        IHttpService httpService,
        ICheckoutUrlPolicy urlPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<HostedCheckoutSessionClient> logger)
    {
        _httpService = httpService;
        _urlPolicy = urlPolicy;
        _options = options;
        _logger = logger;
    }

    public async Task<ProviderSessionCreationResult> CreateSessionAsync(
        PaymentProvider provider,
        HostedCheckoutSessionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!_urlPolicy.IsAllowedProviderEndpoint(provider.ApiBaseUrl))
        {
            _logger.LogError("Payment provider endpoint failed security validation Provider={Provider}", provider.ProviderName);
            return new ProviderSessionCreationResult { Outcome = ProviderClientOutcome.Unavailable };
        }

        var baseUri = new Uri(provider.ApiBaseUrl.EndsWith('/') ? provider.ApiBaseUrl : provider.ApiBaseUrl + "/");
        var url = new Uri(baseUri, "sessions").AbsoluteUri;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["x-api-key"] = provider.ApiKey,
            ["idempotency-key"] = idempotencyKey
        };

        try
        {
            var timeout = Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60);
            var (response, error) = await _httpService.SendRequest<HostedCheckoutSessionResponse>(
                HttpMethod.Post,
                url,
                request,
                "application/json",
                headers,
                cancellationToken,
                timeout);

            if (response is { Id: not null, Url: not null })
            {
                return new ProviderSessionCreationResult { Outcome = ProviderClientOutcome.Success, Response = response };
            }
            if (response != null && (!string.IsNullOrWhiteSpace(response.ErrorCode) || response.Status is >= 400 and < 500))
            {
                return new ProviderSessionCreationResult
                {
                    Outcome = ProviderClientOutcome.Rejected,
                    ProviderErrorCode = ProviderRejectionParser.SanitizeErrorCode(response.ErrorCode)
                };
            }

            if (ProviderRejectionParser.TryGetValidationErrorCode(error, out var providerErrorCode))
            {
                _logger.LogWarning(
                    "Payment session request rejected Provider={Provider} ProviderErrorCode={ProviderErrorCode} Currency={Currency} Country={Country} HasStore={HasStore} HasTheme={HasTheme}",
                    provider.ProviderName,
                    providerErrorCode,
                    request.Amount.Currency,
                    request.CountryCode,
                    !string.IsNullOrWhiteSpace(request.Store),
                    !string.IsNullOrWhiteSpace(request.ThemeId));

                return new ProviderSessionCreationResult
                {
                    Outcome = ProviderClientOutcome.Rejected,
                    ProviderErrorCode = providerErrorCode
                };
            }

            _logger.LogWarning("Payment session request returned no usable response Provider={Provider} HasPackageError={HasPackageError}",
                provider.ProviderName, !string.IsNullOrWhiteSpace(error));
            return new ProviderSessionCreationResult { Outcome = IsUnavailable(error) ? ProviderClientOutcome.Unavailable : ProviderClientOutcome.Failure };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Payment session request timed out Provider={Provider}", provider.ProviderName);
            return new ProviderSessionCreationResult { Outcome = ProviderClientOutcome.Timeout };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError("Payment session request failed Provider={Provider} ExceptionType={ExceptionType}", provider.ProviderName, ex.GetType().Name);
            return new ProviderSessionCreationResult { Outcome = ProviderClientOutcome.Failure };
        }
    }

    private static bool IsUnavailable(string? error) =>
        error?.Contains("circuit", StringComparison.OrdinalIgnoreCase) == true ||
        error?.Contains("unavailable", StringComparison.OrdinalIgnoreCase) == true;

}
