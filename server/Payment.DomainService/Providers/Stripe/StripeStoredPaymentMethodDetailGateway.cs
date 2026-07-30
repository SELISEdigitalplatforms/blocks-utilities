using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Reads a saved card's brand, last four and expiry from Stripe.
/// </summary>
/// <remarks>
/// A PaymentIntent event names the payment method but carries no card details — those live on
/// the charge — so storing a card from the authorization alone would leave the shopper looking
/// at a blank entry. One read per saved card fills that in.
/// </remarks>
public sealed class StripeStoredPaymentMethodDetailGateway :
    IStoredPaymentMethodDetailProviderGateway
{
    private readonly IHttpService _httpService;
    private readonly StripeEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<StripeStoredPaymentMethodDetailGateway> _logger;

    public StripeStoredPaymentMethodDetailGateway(
        IHttpService httpService,
        StripeEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<StripeStoredPaymentMethodDetailGateway> logger)
    {
        _httpService = httpService;
        _endpointPolicy = endpointPolicy;
        _options = options;
        _logger = logger;
    }

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.StripeProvider,
            StringComparison.OrdinalIgnoreCase);

    public async Task<StoredPaymentMethodDetail?> GetAsync(
        PaymentProvider provider,
        string providerToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(providerToken))
        {
            return null;
        }

        var url = StripeUrl.Build(
            provider.ApiBaseUrl,
            $"v1/payment_methods/{Uri.EscapeDataString(providerToken)}");

        try
        {
            var (method, error) = await _httpService.SendRequest<StripePaymentMethod>(
                HttpMethod.Get,
                url,
                null!,
                "application/x-www-form-urlencoded",
                StripeRequestHeaders.Read(provider),
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

            if (method is { Id: not null, Error: null })
            {
                return new StoredPaymentMethodDetail(
                    method.Type,
                    method.Card?.Brand,
                    method.Card?.LastFour,
                    method.Card?.ExpiryMonthText,
                    method.Card?.ExpiryYearText,
                    method.Card?.Funding,
                    method.Card?.Country);
            }

            _logger.LogWarning(
                "Stored payment method detail lookup returned no usable response Provider={Provider} HasPackageError={HasPackageError}",
                PaymentLogValue.Label(provider.ProviderName),
                !string.IsNullOrWhiteSpace(error));

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Stored payment method detail lookup failed Provider={Provider} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return null;
        }
    }
}
