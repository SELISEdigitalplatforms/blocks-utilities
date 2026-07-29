using MongoDB.Bson;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Builds the Checkout Session request Stripe will be sent, and stores it as form fields so a
/// recovered initiation replays byte-identical.
/// </summary>
public sealed class StripeInitiationRequestFactory : IProviderInitiationRequestFactory
{
    /// <summary>
    /// Stripe substitutes this literal into the return URL. It must survive unencoded, so it
    /// is appended after the signed state rather than passed through URL building.
    /// </summary>
    private const string SessionIdTemplate = "session_id={CHECKOUT_SESSION_ID}";

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.StripeProvider,
            StringComparison.OrdinalIgnoreCase);

    public ProviderInitiationRequest Create(
        MakePaymentRequest request,
        PaymentExecutionContext context,
        PaymentDetail payment,
        PaymentProvider provider,
        string returnUrl,
        string providerReference,
        string shopperReference,
        bool includeStoredPaymentMethods,
        long minorUnits)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(provider);

        var successUrl = AppendSessionIdTemplate(returnUrl);
        var captureMode = ResolveCaptureMode(provider);

        var form = new StripeForm()
            .Add("mode", "payment")
            .Add("success_url", successUrl)
            .Add("cancel_url", successUrl)
            .Add("client_reference_id", Truncate(providerReference))
            .Add("customer_email", request.CustomerEmail)
            .AddArrayItem("line_items", 0, item => item
                .Add("quantity", 1)
                .AddObject("price_data", price => price
                    .Add("currency", payment.CurrencyCode.ToLowerInvariant())
                    .Add("unit_amount", minorUnits)
                    .AddObject("product_data", product => product
                        .Add("name", ResolveLineItemName(request, payment)))))
            .AddMetadata(
                RoutingMetadata(context, payment, provider, providerReference, shopperReference));

        if (request.ShouldSavePaymentMethod)
        {
            // Saving a card needs a Customer to attach it to, and Checkout in payment mode
            // does not create one unless asked.
            form.Add("customer_creation", "always");
        }

        form.AddObject("payment_intent_data", intent =>
        {
            if (captureMode == PaymentCaptureModes.Manual)
            {
                intent.Add("capture_method", "manual");
            }

            if (request.ShouldSavePaymentMethod)
            {
                intent.Add("setup_future_usage", "off_session");
            }

            // Session metadata does not propagate to the PaymentIntent, and the events that
            // report the money are raised against the intent. Without this copy those events
            // would arrive with nothing to route them home.
            intent.AddMetadata(
                RoutingMetadata(
                    context,
                    payment,
                    provider,
                    providerReference,
                    shopperReference));
        });

        return new ProviderInitiationRequest
        {
            ProviderName = provider.ProviderName,
            Reference = providerReference,
            MerchantAccount = provider.MerchantId,
            AmountMinorUnits = minorUnits,
            CurrencyCode = payment.CurrencyCode,
            ReturnUrl = successUrl,
            CaptureMode = captureMode,
            CaptureDelayHours = null,
            SiteId = provider.SiteId,
            Payload = ToPayload(form)
        };
    }

    /// <summary>
    /// What Stripe echoes back on every object derived from this checkout, so an inbound
    /// event can be routed to its tenant and payment.
    /// </summary>
    private static Dictionary<string, string?> RoutingMetadata(
        PaymentExecutionContext context,
        PaymentDetail payment,
        PaymentProvider provider,
        string providerReference,
        string shopperReference) => new()
    {
        ["tenant_reference"] = providerReference,
        ["payment_id"] = payment.ItemId,
        ["merchant_account"] = provider.MerchantId,
        ["organization_id"] = context.OrganizationId,

        // Stripe has no shopper field to echo, so the reference that owns any saved card has
        // to travel as metadata. Without it an authorization event cannot say whose card was
        // stored, and the card is never recorded.
        [StripeRoutingMetadata.ShopperReferenceKey] = shopperReference
    };

    /// <summary>Recovers the form fields from a stored envelope.</summary>
    public static Dictionary<string, string> ReadForm(ProviderInitiationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Payload.Elements.ToDictionary(
            element => element.Name,
            element => element.Value.AsString,
            StringComparer.Ordinal);
    }

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

    /// <summary>
    /// Stripe has no merchant-level capture delay, so its only choice is immediate capture or
    /// authorize-now-capture-later.
    /// </summary>
    private static string ResolveCaptureMode(PaymentProvider provider) =>
        provider.ManualCapture
            ? PaymentCaptureModes.Manual
            : PaymentCaptureModes.AutomaticImmediate;

    /// <summary>
    /// Shown to the shopper on Stripe's page. Falls back to the order id rather than leaving
    /// the line item unnamed, which Stripe rejects.
    /// </summary>
    private static string ResolveLineItemName(
        MakePaymentRequest request,
        PaymentDetail payment) =>
        !string.IsNullOrWhiteSpace(request.Description)
            ? request.Description
            : !string.IsNullOrWhiteSpace(request.OrderId)
                ? request.OrderId
                : payment.ItemId;

    private static string Truncate(string value) =>
        value.Length <= StripeConstants.MaximumClientReferenceLength
            ? value
            : value[..StripeConstants.MaximumClientReferenceLength];
}
