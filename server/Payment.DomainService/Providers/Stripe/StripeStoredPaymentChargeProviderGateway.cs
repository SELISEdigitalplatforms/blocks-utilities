using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.StoredPayment;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Charges a saved card with no shopper present.
/// </summary>
/// <remarks>
/// One call: a PaymentIntent created and confirmed together, naming the customer the card is
/// attached to and the payment method itself. Stripe refuses a saved payment method that is not
/// named alongside its customer, which is why <see cref="StoredPaymentMethod.ProviderPayerReference"/>
/// is stored rather than derived.
/// <para>
/// The mandate this relies on is established at the original payment, by
/// <c>setup_future_usage=off_session</c> on that checkout. Without it Stripe declines here with
/// <c>authentication_required</c> — the card is fine, but nobody is present to authenticate it.
/// </para>
/// </remarks>
public sealed class StripeStoredPaymentChargeProviderGateway :
    IStoredPaymentChargeProviderGateway
{
    private readonly IHttpService _httpService;
    private readonly StripeEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<StripeStoredPaymentChargeProviderGateway> _logger;

    public StripeStoredPaymentChargeProviderGateway(
        IHttpService httpService,
        StripeEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<StripeStoredPaymentChargeProviderGateway> logger)
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

    public async Task<StoredPaymentChargeProviderResult> ChargeAsync(
        PaymentProvider provider,
        StoredPaymentChargeRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);

        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl))
        {
            return new StoredPaymentChargeProviderResult(
                StoredPaymentChargeOutcome.Unavailable,
                SafeErrorCode: "provider_endpoint_invalid");
        }

        if (string.IsNullOrWhiteSpace(request.ProviderPayerReference))
        {
            // Stored before the customer id was recorded, so there is nothing to charge
            // against. Terminal rather than retryable: waiting will not produce one, and the
            // shopper has to save the card again.
            _logger.LogWarning(
                "A stored card cannot be charged off-session because no provider payer reference was recorded Provider={Provider}",
                PaymentLogValue.Label(provider.ProviderName));

            return new StoredPaymentChargeProviderResult(
                StoredPaymentChargeOutcome.Rejected,
                SafeErrorCode: "provider_payer_reference_missing");
        }

        var url = StripeUrl.Build(provider.ApiBaseUrl, "v1/payment_intents");
        var form = new StripeForm()
            .Add("amount", request.Amount.Value)
            .Add("currency", request.Amount.Currency.ToLowerInvariant())
            .Add("customer", request.ProviderPayerReference)
            .Add("payment_method", request.PaymentMethod.StoredPaymentMethodId)
            // Together these make it a merchant-initiated charge: confirmed immediately rather
            // than handed to a browser, and declared as happening with nobody present so Stripe
            // applies the saved mandate instead of trying to authenticate.
            .Add("off_session", "true")
            .Add("confirm", "true")
            .AddMetadata(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [StripeRoutingMetadata.ReferenceKey] = request.Reference,
                    [StripeRoutingMetadata.MerchantAccountKey] = request.MerchantAccount,
                    [StripeRoutingMetadata.ShopperReferenceKey] = request.ShopperReference,
                    [StripeRoutingMetadata.OrganizationKey] =
                        request.Metadata.OrganizationId
                });

        try
        {
            var (intent, error) = await _httpService.SendFormUrlEncoded<StripePaymentIntent>(
                HttpMethod.Post,
                form.Fields,
                url,
                StripeRequestHeaders.Create(provider, idempotencyKey),
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

            if (intent is { Id: not null, Error: null })
            {
                return ResolveOutcome(intent.Status) switch
                {
                    StoredPaymentChargeOutcome.Accepted =>
                        new StoredPaymentChargeProviderResult(
                            StoredPaymentChargeOutcome.Accepted,
                            intent.Id,
                            intent.Status),
                    _ => new StoredPaymentChargeProviderResult(
                        StoredPaymentChargeOutcome.Rejected,
                        intent.Id,
                        intent.Status,
                        ProviderRejectionParser.SanitizeErrorCode(intent.Status))
                };
            }

            if (intent?.Error != null)
            {
                return new StoredPaymentChargeProviderResult(
                    StripeProviderOutcome.ToCharge(StripeOutcomeMapper.Map(intent.Error)),
                    // Stripe returns the intent it created even when confirmation declines, so
                    // the reference is worth keeping: it is what later events name.
                    intent.Error.PaymentIntentId,
                    SafeErrorCode: StripeOutcomeMapper.SafeCode(intent.Error));
            }

            if (ProviderRejectionParser.TryGetValidationErrorCode(error, out var safeErrorCode))
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
                StripeUnavailable.IsTransient(error)
                    ? StoredPaymentChargeOutcome.Unavailable
                    : StoredPaymentChargeOutcome.OutcomeUnknown);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The charge may well have been taken; only a later event can say.
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
                "Stored payment charge provider call failed Provider={Provider} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return new StoredPaymentChargeProviderResult(
                StoredPaymentChargeOutcome.OutcomeUnknown);
        }
    }

    /// <summary>
    /// What the confirmed intent's state means for the charge.
    /// </summary>
    /// <remarks>
    /// <c>succeeded</c> and <c>processing</c> are both accepted: the charge is under way and the
    /// webhook decides the rest. <c>requires_action</c> is the one that looks like success and
    /// is not — it means the card wants the shopper to authenticate, which is impossible
    /// off-session, so it is terminal here rather than something to wait on.
    /// </remarks>
    private static StoredPaymentChargeOutcome ResolveOutcome(string? status) => status switch
    {
        "succeeded" => StoredPaymentChargeOutcome.Accepted,
        "processing" => StoredPaymentChargeOutcome.Accepted,
        "requires_capture" => StoredPaymentChargeOutcome.Accepted,
        _ => StoredPaymentChargeOutcome.Rejected
    };
}
