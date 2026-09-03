using System.Text;
using MongoDB.Bson;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Adyen;

public sealed class AdyenInitiationRequestFactory : IProviderInitiationRequestFactory
{
    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.AdyenOnlineProvider,
            StringComparison.OrdinalIgnoreCase);

    public ProviderInitiationRequest Create(
        MakePaymentRequest request,
        PaymentExecutionContext context,
        PaymentDetail payment,
        PaymentProvider provider,
        string returnUrl,
        string providerReference,
        string shopperReference,
        // Unused: Adyen addresses the shopper by shopperReference and looks their stored
        // methods up from it, so it has no separate payer identifier to carry.
        string? providerPayerReference,
        bool includeStoredPaymentMethods,
        long minorUnits)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(provider);

        var sendShopperReference =
            includeStoredPaymentMethods ||
            request.ShouldSavePaymentMethod;

        var session = new HostedCheckoutSessionRequest
        {
            MerchantAccount = provider.MerchantId,
            Store = provider.StoreId,
            Amount = new ProviderAmount
            {
                Value = minorUnits,
                Currency = payment.CurrencyCode
            },
            ReturnUrl = returnUrl,
            Reference = providerReference,
            Mode = "hosted",
            ThemeId = provider.ThemeId,
            CountryCode = provider.CountryCode ?? request.CustomerCountry ?? string.Empty,
            CaptureDelayHours = provider.ManualCapture
                ? null
                : provider.CaptureDelayHours,
            AdditionalData = new ProviderAdditionalData
            {
                ManualCapture = provider.ManualCapture
            },
            Metadata = new ProviderMetadata
            {
                TenantReference = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(payment.TenantId)),
                SiteId = provider.SiteId,

                // The payment's organization, not the caller's — the two differ whenever the
                // console takes a payment for another organization. Intake compares what comes
                // back against the payment's own, so echoing the caller's makes every one of
                // those webhooks unauthorized and leaves the payment in Processing for good.
                OrganizationId = payment.OrganizationId
            },
            StorePaymentMethodMode = request.ShouldSavePaymentMethod
                ? "askForConsent"
                : "disabled",
            // Subscription checkout declares its own model explicitly (see MakePaymentRequest.
            // RecurringModel and the validator that limits it to that one caller); any other
            // caller that saves a token here keeps the long-standing CardOnFile default -- a
            // shopper-initiated reuse, not a merchant-driven schedule. Not verified against a
            // live Adyen sandbox in this environment: this follows
            // https://docs.adyen.com/online-payments/tokenization/make-token-payments, which
            // documents Subscription as the correct model for fixed-schedule, merchant-initiated
            // charges (what a subscription renewal is) as opposed to CardOnFile's
            // shopper-initiated ones, but the actual Adyen sandbox behaviour was not exercised.
            RecurringProcessingModel = sendShopperReference
                ? (request.RecurringModel is { Length: > 0 } requestedModel
                    ? requestedModel
                    : PaymentConstants.AdyenCardOnFileRecurringModel)
                : null,
            ShopperReference = sendShopperReference
                ? shopperReference
                : null,
            ShopperEmail = request.CustomerEmail,
            ShopperInteraction = "Ecommerce"
        };

        return new ProviderInitiationRequest
        {
            ProviderName = provider.ProviderName,
            Reference = session.Reference,
            MerchantAccount = session.MerchantAccount,
            AmountMinorUnits = minorUnits,
            CurrencyCode = payment.CurrencyCode,
            ReturnUrl = returnUrl,
            CaptureMode = ResolveCaptureMode(provider),
            CaptureDelayHours = session.CaptureDelayHours,
            SiteId = session.Metadata.SiteId,
            Payload = session.ToBsonDocument()
        };
    }

    /// <summary>
    /// Adyen expresses capture through a manual-capture flag plus an optional delay, with no
    /// delay meaning "whatever the merchant account defaults to". Translate that into the
    /// capture mode the rest of the system reasons about.
    /// </summary>
    private static string ResolveCaptureMode(PaymentProvider provider) =>
        provider.ManualCapture
            ? PaymentCaptureModes.Manual
            : provider.CaptureDelayHours switch
            {
                0 => PaymentCaptureModes.AutomaticImmediate,
                > 0 => PaymentCaptureModes.AutomaticDelayed,
                _ => PaymentCaptureModes.AccountDefault
            };

    /// <summary>Recovers the Adyen request body from a stored envelope.</summary>
    public static HostedCheckoutSessionRequest ReadSession(
        ProviderInitiationRequest request) =>
        MongoDB.Bson.Serialization.BsonSerializer
            .Deserialize<HostedCheckoutSessionRequest>(request.Payload);
}
