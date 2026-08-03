using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.HostedCheckout;

public sealed class HostedCheckoutStoredPaymentMethodProviderGateway :
    IStoredPaymentMethodProviderGateway
{
    private readonly IHttpService _httpService;
    private readonly AdyenEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<
        HostedCheckoutStoredPaymentMethodProviderGateway> _logger;

    public HostedCheckoutStoredPaymentMethodProviderGateway(
        IHttpService httpService,
        AdyenEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<
            HostedCheckoutStoredPaymentMethodProviderGateway> logger)
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

    public async Task<StoredPaymentMethodRemovalOutcome> RemoveAsync(
        PaymentProvider provider,
        StoredPaymentMethod method,
        string providerToken,
        CancellationToken cancellationToken)
    {
        if (!_endpointPolicy.IsAllowed(
                provider.ApiBaseUrl))
        {
            return StoredPaymentMethodRemovalOutcome
                .OperationalFailure;
        }

        var baseUri = new Uri(
            provider.ApiBaseUrl.EndsWith('/')
                ? provider.ApiBaseUrl
                : provider.ApiBaseUrl + "/");
        var path =
            $"storedPaymentMethods/{Uri.EscapeDataString(providerToken)}";
        var requestUrl =
            new Uri(baseUri, path).AbsoluteUri +
            $"?merchantAccount={Uri.EscapeDataString(provider.MerchantId)}" +
            $"&shopperReference={Uri.EscapeDataString(method.ShopperReference)}";

        try
        {
            var (_, error) = await _httpService.Delete<object>(
                requestUrl,
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["x-api-key"] = provider.ApiKey
                },
                cancellationToken,
                Math.Clamp(
                    _options.CurrentValue.ProviderTimeoutSeconds,
                    1,
                    60));

            if (string.IsNullOrWhiteSpace(error) ||
                IsAlreadyRemoved(error))
            {
                return StoredPaymentMethodRemovalOutcome.Removed;
            }

            _logger.LogWarning(
                "Stored payment method provider removal returned an unusable response Provider={Provider} ErrorCategory={ErrorCategory}",
                PaymentLogValue.Label(provider.ProviderName),
                ClassifyError(error));

            return IsDefinitiveOperationalFailure(error)
                ? StoredPaymentMethodRemovalOutcome.OperationalFailure
                : StoredPaymentMethodRemovalOutcome.OutcomeUnknown;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return StoredPaymentMethodRemovalOutcome.OutcomeUnknown;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Stored payment method provider removal failed Provider={Provider} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return StoredPaymentMethodRemovalOutcome.OutcomeUnknown;
        }
    }

    private static bool IsAlreadyRemoved(string error) =>
        error.Contains("404", StringComparison.OrdinalIgnoreCase) ||
        error.Contains(
            "not found",
            StringComparison.OrdinalIgnoreCase) ||
        error.Contains(
            "does not exist",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsDefinitiveOperationalFailure(string error) =>
        error.Contains("401", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("403", StringComparison.OrdinalIgnoreCase) ||
        error.Contains(
            "unauthorized",
            StringComparison.OrdinalIgnoreCase) ||
        error.Contains(
            "forbidden",
            StringComparison.OrdinalIgnoreCase);

    private static string ClassifyError(string error)
    {
        if (IsDefinitiveOperationalFailure(error))
        {
            return "authentication";
        }

        if (error.Contains(
                "circuit",
                StringComparison.OrdinalIgnoreCase))
        {
            return "circuit_open";
        }

        if (error.Contains(
                "timeout",
                StringComparison.OrdinalIgnoreCase))
        {
            return "timeout";
        }

        return "provider_failure";
    }
}
