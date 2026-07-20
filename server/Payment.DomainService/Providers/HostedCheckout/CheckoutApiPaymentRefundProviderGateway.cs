using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.Refunds;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.HostedCheckout;

public sealed class CheckoutApiPaymentRefundProviderGateway :
    IPaymentRefundProviderGateway
{
    private readonly IHttpService _httpService;
    private readonly ICheckoutUrlPolicy _urlPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<
        CheckoutApiPaymentRefundProviderGateway> _logger;

    public CheckoutApiPaymentRefundProviderGateway(
        IHttpService httpService,
        ICheckoutUrlPolicy urlPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<CheckoutApiPaymentRefundProviderGateway> logger)
    {
        _httpService = httpService;
        _urlPolicy = urlPolicy;
        _options = options;
        _logger = logger;
    }

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.AdyenOnlineProvider,
            StringComparison.OrdinalIgnoreCase);

    public async Task<PaymentRefundProviderResult> SubmitAsync(
        PaymentProvider provider,
        string originalPaymentPspReference,
        ProviderRefundRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!_urlPolicy.IsAllowedProviderEndpoint(
                provider.ApiBaseUrl))
        {
            return new PaymentRefundProviderResult(
                PaymentRefundProviderOutcome.Unavailable);
        }

        var baseUri = new Uri(
            provider.ApiBaseUrl.EndsWith('/')
                ? provider.ApiBaseUrl
                : provider.ApiBaseUrl + "/");
        var path =
            $"payments/{Uri.EscapeDataString(originalPaymentPspReference)}/refunds";
        var requestUrl = new Uri(baseUri, path).AbsoluteUri;
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
                    ProviderRefundResponse>(
                    HttpMethod.Post,
                    requestUrl,
                    request,
                    "application/json",
                    headers,
                    cancellationToken,
                    Math.Clamp(
                        _options.CurrentValue
                            .ProviderTimeoutSeconds,
                        1,
                        60));

            if (response is
                {
                    PspReference: not null,
                    Reference: not null
                } &&
                string.Equals(
                    response.Reference,
                    request.Reference,
                    StringComparison.Ordinal))
            {
                return new PaymentRefundProviderResult(
                    PaymentRefundProviderOutcome.Submitted,
                    response.PspReference,
                    response.Status);
            }

            if (response != null &&
                !string.IsNullOrWhiteSpace(
                    response.ErrorCode))
            {
                return new PaymentRefundProviderResult(
                    PaymentRefundProviderOutcome.Rejected,
                    SafeErrorCode:
                    ProviderRejectionParser
                        .SanitizeErrorCode(
                            response.ErrorCode));
            }

            if (ProviderRejectionParser
                .TryGetValidationErrorCode(
                    error,
                    out var safeErrorCode))
            {
                return new PaymentRefundProviderResult(
                    PaymentRefundProviderOutcome.Rejected,
                    SafeErrorCode: safeErrorCode);
            }

            _logger.LogWarning(
                "Payment refund returned no usable response Provider={Provider} HasPackageError={HasPackageError}",
                PaymentLogValue.Label(provider.ProviderName),
                !string.IsNullOrWhiteSpace(error));

            return new PaymentRefundProviderResult(
                IsUnavailable(error)
                    ? PaymentRefundProviderOutcome.Unavailable
                    : PaymentRefundProviderOutcome
                        .OutcomeUnknown);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return new PaymentRefundProviderResult(
                PaymentRefundProviderOutcome.Timeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Payment refund provider call failed Provider={Provider} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return new PaymentRefundProviderResult(
                PaymentRefundProviderOutcome
                    .OutcomeUnknown);
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
