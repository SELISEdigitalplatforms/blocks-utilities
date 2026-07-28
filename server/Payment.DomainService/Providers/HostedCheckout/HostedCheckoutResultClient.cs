using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.HostedCheckout;

public sealed class HostedCheckoutResultClient : ICheckoutResultClient
{
    private readonly IHttpService _httpService;
    private readonly AdyenEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<HostedCheckoutResultClient> _logger;

    public HostedCheckoutResultClient(
        IHttpService httpService,
        AdyenEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<HostedCheckoutResultClient> logger)
    {
        _httpService = httpService;
        _endpointPolicy = endpointPolicy;
        _options = options;
        _logger = logger;
    }

    public async Task<CheckoutResultClientResult> GetAsync(
        PaymentProvider provider,
        string sessionId,
        string sessionResult,
        CancellationToken cancellationToken)
    {
        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl))
            return new CheckoutResultClientResult { Outcome = ProviderClientOutcome.Unavailable };

        var baseUri = new Uri(provider.ApiBaseUrl.EndsWith('/') ? provider.ApiBaseUrl : provider.ApiBaseUrl + "/");
        var url = new Uri(baseUri, $"sessions/{Uri.EscapeDataString(sessionId)}?sessionResult={Uri.EscapeDataString(sessionResult)}").AbsoluteUri;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["x-api-key"] = provider.ApiKey };
        try
        {
            var (response, error) = await _httpService.SendRequest<HostedCheckoutResult>(
                HttpMethod.Get,
                url,
                null!,
                "application/json",
                headers,
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));
            if (response is { Id: not null, Reference: not null, Status: not null })
                return new CheckoutResultClientResult { Outcome = ProviderClientOutcome.Success, Response = response };
            if (response != null && !string.IsNullOrWhiteSpace(response.ErrorCode))
                return new CheckoutResultClientResult { Outcome = ProviderClientOutcome.Rejected };
            _logger.LogWarning("Payment session validation returned no usable response Provider={Provider} HasPackageError={HasPackageError}",
                provider.ProviderName, !string.IsNullOrWhiteSpace(error));
            return new CheckoutResultClientResult
            {
                Outcome = error?.Contains("circuit", StringComparison.OrdinalIgnoreCase) == true ||
                          error?.Contains("unavailable", StringComparison.OrdinalIgnoreCase) == true
                    ? ProviderClientOutcome.Unavailable
                    : ProviderClientOutcome.Failure
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CheckoutResultClientResult { Outcome = ProviderClientOutcome.Timeout };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning("Payment session validation failed Provider={Provider} ExceptionType={ExceptionType}", provider.ProviderName, ex.GetType().Name);
            return new CheckoutResultClientResult { Outcome = ProviderClientOutcome.Failure };
        }
    }
}
