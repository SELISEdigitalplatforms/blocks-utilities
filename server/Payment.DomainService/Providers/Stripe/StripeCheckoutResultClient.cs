using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Reads a Checkout Session back so the shopper can be shown the right screen. The result is
/// only ever used for display; the payment's authoritative state comes from webhooks.
/// </summary>
public sealed class StripeCheckoutResultClient : ICheckoutResultClient
{
    private readonly IHttpService _httpService;
    private readonly StripeEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<StripeCheckoutResultClient> _logger;

    public StripeCheckoutResultClient(
        IHttpService httpService,
        StripeEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<StripeCheckoutResultClient> logger)
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

    /// <param name="sessionResult">
    /// Unused. Adyen returns an opaque result token on the redirect; Stripe identifies the
    /// session by id alone.
    /// </param>
    public async Task<CheckoutResultClientResult> GetAsync(
        PaymentProvider provider,
        string sessionId,
        string sessionResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(sessionId))
        {
            return new CheckoutResultClientResult
            {
                Outcome = ProviderClientOutcome.Unavailable
            };
        }

        var url = StripeUrl.Build(
            provider.ApiBaseUrl,
            $"v1/checkout/sessions/{Uri.EscapeDataString(sessionId)}");

        try
        {
            var (session, error) = await _httpService.SendRequest<StripeCheckoutSession>(
                HttpMethod.Get,
                url,
                null!,
                "application/x-www-form-urlencoded",
                StripeRequestHeaders.Read(provider),
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

            if (session is { Id: not null, Error: null })
            {
                return new CheckoutResultClientResult
                {
                    Outcome = ProviderClientOutcome.Success,
                    Response = ToCheckoutResult(session)
                };
            }

            if (session?.Error != null)
            {
                return new CheckoutResultClientResult
                {
                    Outcome = StripeOutcomeMapper.Map(session.Error)
                };
            }

            _logger.LogWarning(
                "Checkout result lookup returned no usable response Provider={Provider} HasPackageError={HasPackageError}",
                PaymentLogValue.Label(provider.ProviderName),
                !string.IsNullOrWhiteSpace(error));

            return new CheckoutResultClientResult
            {
                Outcome = StripeUnavailable.IsTransient(error)
                    ? ProviderClientOutcome.Unavailable
                    : ProviderClientOutcome.Failure
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CheckoutResultClientResult
            {
                Outcome = ProviderClientOutcome.Timeout
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Checkout result lookup failed Provider={Provider} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return new CheckoutResultClientResult
            {
                Outcome = ProviderClientOutcome.Failure
            };
        }
    }

    /// <summary>
    /// Maps onto the shared checkout result so validation and observation stay
    /// provider-neutral. The status carries both Stripe fields, because a completed session
    /// can still be unpaid when a delayed payment method is used.
    /// </summary>
    private static HostedCheckoutResult ToCheckoutResult(StripeCheckoutSession session) => new()
    {
        Id = session.Id,
        Reference = session.ClientReferenceId,
        Status = StripeCheckoutStatusMapper.Compose(session.Status, session.PaymentStatus),
        Amount = session.AmountTotal.HasValue && session.Currency != null
            ? new ProviderAmount
            {
                Value = session.AmountTotal.Value,
                Currency = session.Currency.ToUpperInvariant()
            }
            : null,
        Payments = session.PaymentIntent == null
            ? []
            :
            [
                new HostedCheckoutPayment
                {
                    PspReference = session.PaymentIntent,
                    ResultCode = session.PaymentStatus
                }
            ]
    };
}
