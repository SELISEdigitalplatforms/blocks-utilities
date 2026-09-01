using System.Text;
using MongoDB.Bson;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Requests;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Adyen;

/// <summary>
/// Builds an Adyen hosted-checkout session that collects a card and stores it as a reusable
/// token, without authorising anything.
/// </summary>
/// <remarks>
/// Mirrors <see cref="AdyenInitiationRequestFactory"/>'s <see cref="HostedCheckoutSessionRequest"/>
/// shape rather than inventing a second transport: the same return-state signing, callback
/// routing and result mapping already built for a real Adyen payment apply unchanged to a setup
/// session, and Adyen's own hosted-checkout API takes a zero-value <c>/sessions</c> request for
/// exactly this purpose the same way it takes a priced one.
/// <para>
/// <b>Uncertain, called out rather than guessed at</b>: unlike Stripe -- whose dedicated
/// <c>setup</c> mode is documented to accept and require no amount -- Adyen's Sessions API is not
/// independently verified here to accept <c>amount.value: 0</c> for every payment method Adyen
/// might offer a shopper (some card networks/issuers are documented elsewhere to reject a
/// zero-value authorisation outright). This follows the same zero-value pattern the spec asked
/// for and mirrors <see cref="AdyenInitiationRequestFactory"/>'s existing request shape as closely
/// as possible, but the actual Adyen sandbox behaviour for a zero-value session was not exercised
/// against a live Adyen test merchant in this change -- see the PR description's "not verified
/// live" callout. If Adyen rejects a true zero, the fix is confined to this factory (and possibly
/// <see cref="ProviderInitiationRequest.AmountMinorUnits"/>'s downstream reconciliation, which
/// already treats zero as "nothing was authorised" for Stripe's setup path).
/// </para>
/// </remarks>
public sealed class AdyenSetupSessionRequestFactory : IPaymentMethodSetupRequestFactory
{
    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.AdyenOnlineProvider,
            StringComparison.OrdinalIgnoreCase);

    public ProviderInitiationRequest Create(
        CreatePaymentMethodSetupRequest request,
        PaymentDetail payment,
        PaymentProvider provider,
        string returnUrl,
        string providerReference,
        string shopperReference,
        // Unused, the same reason AdyenInitiationRequestFactory leaves it unused: Adyen addresses
        // the shopper by shopperReference, not by a separate payer identifier.
        string? providerPayerReference)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(provider);

        var session = new HostedCheckoutSessionRequest
        {
            MerchantAccount = provider.MerchantId,
            Store = provider.StoreId,
            Amount = new ProviderAmount
            {
                // Zero, and it stays zero everywhere downstream -- nothing is authorised by a card
                // setup, so there is no figure a later event could be checked against. Same
                // convention StripeSetupSessionRequestFactory's ProviderInitiationRequest uses.
                Value = 0,
                Currency = payment.CurrencyCode
            },
            ReturnUrl = returnUrl,
            Reference = providerReference,
            Mode = "hosted",
            ThemeId = provider.ThemeId,
            CountryCode = provider.CountryCode ?? string.Empty,
            AdditionalData = new ProviderAdditionalData
            {
                ManualCapture = false
            },
            Metadata = new ProviderMetadata
            {
                TenantReference = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(payment.TenantId)),
                SiteId = provider.SiteId,
                OrganizationId = payment.OrganizationId
            },
            // A card-on-file setup's entire purpose is a reusable token, so consent and a shopper
            // reference are always requested -- never conditional the way a priced checkout's
            // ShouldSavePaymentMethod is.
            StorePaymentMethodMode = "askForConsent",
            RecurringProcessingModel = "CardOnFile",
            ShopperReference = shopperReference,
            ShopperEmail = request.CustomerEmail,
            ShopperInteraction = "Ecommerce"
        };

        return new ProviderInitiationRequest
        {
            ProviderName = provider.ProviderName,
            Reference = session.Reference,
            MerchantAccount = session.MerchantAccount,
            AmountMinorUnits = 0,
            CurrencyCode = payment.CurrencyCode,
            ReturnUrl = returnUrl,
            CaptureMode = PaymentCaptureModes.AccountDefault,
            CaptureDelayHours = null,
            SiteId = session.Metadata.SiteId,
            Payload = session.ToBsonDocument()
        };
    }
}
