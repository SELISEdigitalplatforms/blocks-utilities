using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.StoredPayment;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.HostedCheckout;

/// <summary>
/// Charges a stored Adyen card off-session through the Checkout API's <c>/payments</c>.
/// </summary>
/// <remarks>
/// Sent on a plain <see cref="HttpClient"/>, not through the platform's <c>IHttpService</c>. That
/// service retries every 5xx up to three times, and Adyen answers a merchant-configuration refusal
/// (<c>905_1</c>, no acquirer for the card and currency) with HTTP 500: the same POST went out four
/// times, adding six to eleven seconds to a payer's request, and could never succeed. A single
/// attempt is enough here because nothing is lost by stopping: an answer that is not a clear accept
/// or refusal is recorded as unknown and settled by the recovery sweep under the same idempotency
/// key, which is the retry this charge actually needs.
/// </remarks>
public sealed class CheckoutApiStoredPaymentChargeProviderGateway :
    IStoredPaymentChargeProviderGateway
{
    private readonly IHttpClientFactory _httpClients;
    private readonly AdyenEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<CheckoutApiStoredPaymentChargeProviderGateway>
        _logger;

    public CheckoutApiStoredPaymentChargeProviderGateway(
        IHttpClientFactory httpClients,
        AdyenEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<CheckoutApiStoredPaymentChargeProviderGateway> logger)
    {
        _httpClients = httpClients;
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
        var requestUrl = new Uri(baseUri, "payments");

        try
        {
            var (response, error) = await SendAsync(
                requestUrl,
                request,
                provider.ApiKey,
                idempotencyKey,
                cancellationToken);

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

            // A merchant-account setup problem, not an outage. Treated as a rejection so the
            // payment fails once and visibly, instead of being retried against an account that
            // will keep refusing it.
            if (ProviderRejectionParser.TryGetConfigurationErrorCode(
                    error,
                    out var configurationErrorCode))
            {
                _logger.LogWarning(
                    "Stored payment charge refused by the provider's merchant configuration Provider={Provider} ProviderErrorCode={ProviderErrorCode}",
                    PaymentLogValue.Label(provider.ProviderName),
                    configurationErrorCode);

                return new StoredPaymentChargeProviderResult(
                    StoredPaymentChargeOutcome.Rejected,
                    SafeErrorCode: configurationErrorCode,
                    MerchantConfiguration: true);
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

    /// <summary>
    /// One POST, answered as the platform's <c>IHttpService</c> answers it: the parsed body on
    /// success, otherwise the raw error body, which <see cref="ProviderRejectionParser"/> reads.
    /// </summary>
    private async Task<(StoredPaymentChargeResponse? Response, string Error)> SendAsync(
        Uri requestUrl,
        StoredPaymentChargeRequest request,
        string apiKey,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var client = _httpClients.CreateClient(
            nameof(CheckoutApiStoredPaymentChargeProviderGateway));
        // A timeout surfaces as a cancellation the caller did not ask for, which ChargeAsync
        // records as Timeout - an unknown outcome the recovery sweep settles.
        client.Timeout = TimeSpan.FromSeconds(
            Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

        using var message = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json")
        };
        message.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        message.Headers.TryAddWithoutValidation("idempotency-key", idempotencyKey);

        using var response = await client.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return (null, body);
        }

        try
        {
            return (JsonSerializer.Deserialize<StoredPaymentChargeResponse>(body), string.Empty);
        }
        catch (JsonException)
        {
            return (null, "Error deserializing response");
        }
    }

    private static bool IsUnavailable(string? error) =>
        error?.Contains(
            "unavailable",
            StringComparison.OrdinalIgnoreCase) == true;
}
