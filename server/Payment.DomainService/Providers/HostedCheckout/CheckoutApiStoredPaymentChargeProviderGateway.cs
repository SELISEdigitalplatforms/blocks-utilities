using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.StoredPayment;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.HostedCheckout;

public sealed class CheckoutApiStoredPaymentChargeProviderGateway :
    IStoredPaymentChargeProviderGateway
{
    private readonly IHttpService _httpService;
    private readonly AdyenEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<CheckoutApiStoredPaymentChargeProviderGateway>
        _logger;

    public CheckoutApiStoredPaymentChargeProviderGateway(
        IHttpService httpService,
        AdyenEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<CheckoutApiStoredPaymentChargeProviderGateway> logger)
    {
        _httpService = httpService;
        _endpointPolicy = endpointPolicy;
        _options = options;
        _logger = logger;
    }

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.AdyenOnlineProvider,
            StringComparison.OrdinalIgnoreCase);

    public async Task<StoredPaymentChargeProviderResult> ChargeAsync(
        PaymentProvider provider,
        StoredPaymentChargeRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl))
        {
            return new StoredPaymentChargeProviderResult(
                StoredPaymentChargeOutcome.Unavailable,
                SafeErrorCode: "provider_endpoint_invalid");
        }

        var baseUri = new Uri(
            provider.ApiBaseUrl.EndsWith('/')
                ? provider.ApiBaseUrl
                : provider.ApiBaseUrl + "/");
        var requestUrl = new Uri(baseUri, "payments").AbsoluteUri;
        var headers = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["x-api-key"] = provider.ApiKey,
            ["idempotency-key"] = idempotencyKey
        };

        try
        {
            var (response, error) =
                await _httpService.SendRequest<
                    StoredPaymentChargeResponse>(
                    HttpMethod.Post,
                    requestUrl,
                    request,
                    "application/json",
                    headers,
                    cancellationToken,
                    Math.Clamp(
                        _options.CurrentValue.ProviderTimeoutSeconds,
                        1,
                        60));

            if (response is
                {
                    PspReference: not null,
                    MerchantReference: not null
                } &&
                string.Equals(
                    response.MerchantReference,
                    request.Reference,
                    StringComparison.Ordinal) &&
                response.Amount?.Value == request.Amount.Value &&
                string.Equals(
                    response.Amount.Currency,
                    request.Amount.Currency,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new StoredPaymentChargeProviderResult(
                    StoredPaymentChargeOutcome.Accepted,
                    response.PspReference,
                    response.ResultCode);
            }

            if (response != null &&
                (!string.IsNullOrWhiteSpace(response.ErrorCode) ||
                 response.Status is >= 400 and < 500))
            {
                return new StoredPaymentChargeProviderResult(
                    StoredPaymentChargeOutcome.Rejected,
                    SafeErrorCode:
                    ProviderRejectionParser.SanitizeErrorCode(
                        response.ErrorCode));
            }

            if (ProviderRejectionParser.TryGetValidationErrorCode(
                    error,
                    out var safeErrorCode))
            {
                return new StoredPaymentChargeProviderResult(
                    StoredPaymentChargeOutcome.Rejected,
                    SafeErrorCode: safeErrorCode);
            }

            _logger.LogWarning(
                "Stored payment charge returned no usable response Provider={Provider} HasPackageError={HasPackageError}",
                PaymentLogValue.Label(provider.ProviderName),
                !string.IsNullOrWhiteSpace(error));

            return new StoredPaymentChargeProviderResult(
                IsUnavailable(error)
                    ? StoredPaymentChargeOutcome.Unavailable
                    : StoredPaymentChargeOutcome.OutcomeUnknown);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return new StoredPaymentChargeProviderResult(
                StoredPaymentChargeOutcome.Timeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Stored payment charge failed Provider={Provider} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return new StoredPaymentChargeProviderResult(
                StoredPaymentChargeOutcome.OutcomeUnknown);
        }
    }

    private static bool IsUnavailable(string? error) =>
        error?.Contains(
            "circuit",
            StringComparison.OrdinalIgnoreCase) == true ||
        error?.Contains(
            "unavailable",
            StringComparison.OrdinalIgnoreCase) == true;
}
