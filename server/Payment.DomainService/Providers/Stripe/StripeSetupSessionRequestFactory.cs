using MongoDB.Bson;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Requests;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Builds a Stripe Checkout Session in <c>setup</c> mode: a card is collected and stored, and
/// nothing is charged.
/// </summary>
/// <remarks>
/// Deliberately not a one-cent PaymentIntent, and not a zero-value one either. A token charge
/// appears on the cardholder's statement and has to be refunded; a zero-value PaymentIntent is
/// rejected outright. Setup mode is the operation Stripe provides for this, and the SetupIntent
/// it produces is what carries the off-session mandate the first renewal will rely on.
/// </remarks>
public sealed class StripeSetupSessionRequestFactory : IPaymentMethodSetupRequestFactory
{
    /// <inheritdoc cref="StripeInitiationRequestFactory"/>
    private const string SessionIdTemplate = "sessionId={CHECKOUT_SESSION_ID}";

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.StripeProvider,
            StringComparison.OrdinalIgnoreCase);

    public ProviderInitiationRequest Create(
        CreatePaymentMethodSetupRequest request,
        PaymentDetail payment,
        PaymentProvider provider,
        string returnUrl,
        string providerReference,
        string shopperReference,
        string? providerPayerReference)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(provider);

        var successUrl = AppendSessionIdTemplate(returnUrl);
        var metadata = RoutingMetadata(payment, provider, providerReference, shopperReference);

        var form = new StripeForm()
            .Add("mode", "setup")
            .Add("success_url", successUrl)
            .Add("cancel_url", successUrl)
            .Add("client_reference_id", Truncate(providerReference))
            // Required in setup mode: Stripe decides which payment methods to offer from the
            // currency the card will later be charged in, and there is no amount to infer it
            // from.
            .Add("currency", payment.CurrencyCode.ToLowerInvariant())
            .Add(
                "customer_email",
                string.IsNullOrWhiteSpace(providerPayerReference)
                    ? request.CustomerEmail
                    : null)
            .AddMetadata(metadata);

        if (!string.IsNullOrWhiteSpace(providerPayerReference))
        {
            // The same reason payment mode names a known customer: without it a returning
            // shopper becomes a second Stripe customer, and the card is saved somewhere the
            // subscription's billing account will never look.
            //
            // No customer_creation counterpart is needed. Setup mode has nowhere to attach a
            // payment method except a Customer, so Stripe always makes one when none is named.
            form.Add("customer", providerPayerReference);
        }

        form.AddObject("setup_intent_data", intent =>
        {
            // Session metadata does not reach the SetupIntent, and setup_intent.succeeded is
            // raised against the intent. Without this copy the one event that says the card was
            // stored arrives with nothing to route it home — which is the same defect the
            // payment path has already been bitten by on payment_intent events.
            intent.AddMetadata(metadata);

            if (!string.IsNullOrWhiteSpace(request.Description))
            {
                intent.Add("description", request.Description);
            }
        });

        return new ProviderInitiationRequest
        {
            ProviderName = provider.ProviderName,
            Reference = providerReference,
            MerchantAccount = provider.MerchantId,
            // Zero, and it stays zero everywhere downstream. Nothing is authorised here, so
            // there is no figure a later event could be checked against.
            AmountMinorUnits = 0,
            CurrencyCode = payment.CurrencyCode,
            ReturnUrl = successUrl,
            CaptureMode = PaymentCaptureModes.AccountDefault,
            CaptureDelayHours = null,
            SiteId = provider.SiteId,
            Payload = ToPayload(form)
        };
    }

    /// <summary>
    /// What Stripe echoes back on the session and on the SetupIntent, so an inbound event can be
    /// routed to its tenant, its payment record and the shopper whose card was stored.
    /// </summary>
    private static Dictionary<string, string?> RoutingMetadata(
        PaymentDetail payment,
        PaymentProvider provider,
        string providerReference,
        string shopperReference) => new()
    {
        ["tenant_reference"] = providerReference,
        ["payment_id"] = payment.ItemId,
        ["merchant_account"] = provider.MerchantId,
        [StripeRoutingMetadata.OrganizationKey] = payment.OrganizationId,
        [StripeRoutingMetadata.ShopperReferenceKey] = shopperReference
    };

    private static BsonDocument ToPayload(StripeForm form)
    {
        var payload = new BsonDocument();

        foreach (var (key, value) in form.Fields)
        {
            payload.Add(key, value);
        }

        return payload;
    }

    private static string AppendSessionIdTemplate(string returnUrl) =>
        returnUrl.Contains('?', StringComparison.Ordinal)
            ? $"{returnUrl}&{SessionIdTemplate}"
            : $"{returnUrl}?{SessionIdTemplate}";

    private static string Truncate(string value) =>
        value.Length <= StripeConstants.MaximumClientReferenceLength
            ? value
            : value[..StripeConstants.MaximumClientReferenceLength];
}
