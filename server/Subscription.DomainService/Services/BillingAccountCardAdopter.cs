using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace Subscription.DomainService.Services;

/// <summary>
/// Wires the card a hosted checkout saved to the subscription's billing account, so later
/// off-session charges -- renewals, plan changes, overage, credit top-ups -- have a card to present.
/// </summary>
/// <remarks>
/// Shared by subscription activation and by credit top-up checkout (spec 003), so the rules that
/// took several incidents to get right -- which shopper's cards to look in, and which cards are
/// usable at all -- live in one place.
/// </remarks>
public sealed class BillingAccountCardAdopter
{
    private readonly IStoredPaymentMethodRepository _storedMethods;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly ILogger _logger;

    public BillingAccountCardAdopter(
        IStoredPaymentMethodRepository storedMethods,
        IBillingAccountRepository billingAccounts,
        ILogger<BillingAccountCardAdopter> logger)
        : this(storedMethods, billingAccounts, (ILogger)logger)
    {
    }

    internal BillingAccountCardAdopter(
        IStoredPaymentMethodRepository storedMethods,
        IBillingAccountRepository billingAccounts,
        ILogger logger)
    {
        _storedMethods = storedMethods;
        _billingAccounts = billingAccounts;
        _logger = logger;
    }

    /// <summary>
    /// Whether a stored card can be wired to the billing account as the one a renewal will charge.
    /// </summary>
    /// <remarks>
    /// Stripe charges a card off-session through its customer, so a Stripe card with no customer id
    /// is unusable and must not be adopted. Adyen has no separate customer — it addresses the
    /// shopper by the shopper reference the card is already filed under — so its cards never carry
    /// one. Requiring a customer id of every provider made adoption impossible for Adyen: a paid
    /// checkout activated with no default card (so <c>hasPaymentMethod</c> read false next to an
    /// active card), and a card setup could never complete, being held open until the card was
    /// adopted, so the recovery sweep retried it indefinitely.
    /// </remarks>
    internal static bool IsAdoptable(StoredPaymentMethod method) =>
        method.ProviderPayerReference is { Length: > 0 } ||
        string.Equals(
            method.ProviderName,
            PaymentConstants.AdyenOnlineProvider,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Records the provider's customer and the card from the payment that saved it.
    /// </summary>
    /// <remarks>
    /// Taken from the payment rather than created up front: hosted checkout makes its own
    /// customer, so pre-creating one we cannot pin to the session would leave an orphan behind
    /// on every signup.
    /// </remarks>
    /// <param name="onlyIfNoDefault">
    /// True for a credit top-up: the card becomes the default only while the account has none, so
    /// a card the subscriber added meanwhile is never replaced. Finding one already there is not a
    /// failure.
    /// </param>
    /// <returns>Null once the card is recorded (or, with <paramref name="onlyIfNoDefault"/>, one
    /// already was); otherwise why it was not, as an audit error code.</returns>
    public async Task<string?> AdoptAsync(
        SubscriptionDetail subscription,
        PaymentDetail payment,
        bool onlyIfNoDefault,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.ShopperReference))
        {
            _logger.LogWarning(
                "A paid subscription has no shopper reference to find its card by; renewals " +
                "will fail until one is recorded");

            return "no_shopper_reference";
        }

        // Found by the reference the card was saved under, not by a link from the payment.
        // Hosted checkout never writes StoredPaymentMethodPublicId — only a charge already made
        // from a stored card does — so reading it here meant every subscription reached its
        // first renewal with no card, and failed closed a whole billing period after the
        // mistake was made.
        //
        // Scoped to the *subscriber*, never to the organization that took the money. Those are
        // two different things on every payment this sees: OrganizationId is the merchant scope
        // the provider was resolved under, and is the same value for every subscriber a tenant
        // sells to. Looking cards up by it returns whichever card was saved most recently anywhere
        // under that merchant, so each organization's billing account adopted a stranger's card
        // and provider customer, and every later off-session charge -- renewal, plan change,
        // quantity change, usage overage, all of which read DefaultPaymentMethodId -- presented a
        // customer that does not own the subscription. StoredPaymentMethodRepository.
        // BuildActiveFilter matches the reference and the organization as a pair precisely to keep
        // one organization's cards away from another's; passing the merchant here handed it the
        // wrong half of that pair.
        var methods = await _storedMethods.ListActiveAsync(
            subscription.TenantId,
            [new StoredPaymentMethodLookupScope(
                payment.ShopperReference,
                subscription.OrganizationId)],
            cancellationToken);

        // The card this charge saved, newest first: a shopper who paid twice has more than one,
        // and the one they just used is the one a renewal should charge.
        var method = (methods ?? [])
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .FirstOrDefault(IsAdoptable);

        if (method is null)
        {
            _logger.LogWarning(
                "No provider customer recorded for a subscription; renewals will need one");

            return "no_provider_customer";
        }

        if (onlyIfNoDefault)
        {
            // False means the account already had a card, or is missing. A missing account cannot
            // be repaired by retrying, and an existing card is the outcome wanted, so neither is
            // reported as a failure to retry.
            var adopted = await _billingAccounts.TrySetDefaultPaymentMethodIfNoneAsync(
                subscription.TenantId,
                subscription.BillingAccountId,
                method.ProviderPayerReference,
                method.ItemId,
                payment.OrganizationId,
                cancellationToken);

            _logger.LogInformation(
                "Card saved by a credit top-up checkout {Outcome} TenantId={TenantId} SubscriptionId={SubscriptionId}",
                adopted ? "adopted as the default" : "not adopted; the account already had a default",
                PaymentLogValue.Id(subscription.TenantId),
                PaymentLogValue.Id(subscription.ItemId));

            return null;
        }

        var outcome = await _billingAccounts.TrySetProviderCustomerAsync(
            subscription.TenantId,
            subscription.BillingAccountId,
            // Null for a provider with no separate payer (Adyen): the card is still recorded as
            // the account's default, which is what the renewal reads.
            method.ProviderPayerReference,
            method.ItemId,
            // The scope that took the money, which is what later charges must resolve the
            // provider under. Organizations subscribe; the tenant is the merchant.
            payment.OrganizationId,
            cancellationToken);

        // Never discarded. Whether the card a renewal will present actually got recorded is the
        // one thing this method exists to decide, and the silence here is what let a billing
        // account sit for a month pointing at a removed card while everything else read healthy.
        switch (outcome)
        {
            case SetProviderCustomerOutcome.Repointed:
                _logger.LogWarning(
                    "A subscription's billing account moved to a different provider customer; " +
                    "cards saved against the previous one are no longer reachable " +
                    "TenantId={TenantId} SubscriptionId={SubscriptionId}",
                    PaymentLogValue.Id(subscription.TenantId),
                    PaymentLogValue.Id(subscription.ItemId));
                break;

            case SetProviderCustomerOutcome.AccountMissing:
                _logger.LogError(
                    "A paid subscription has no billing account to record its card against; " +
                    "renewals will find no payment method " +
                    "TenantId={TenantId} SubscriptionId={SubscriptionId}",
                    PaymentLogValue.Id(subscription.TenantId),
                    PaymentLogValue.Id(subscription.ItemId));
                break;
        }

        return outcome == SetProviderCustomerOutcome.AccountMissing ? "billing_account_missing" : null;
    }
}
