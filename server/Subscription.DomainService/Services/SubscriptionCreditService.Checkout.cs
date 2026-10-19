using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Checkout on first top-up (spec 003): a credit purchase on a subscription with no saved card is
/// paid at a hosted checkout that also saves the card.
/// </summary>
/// <remarks>
/// Settled from this purchase record, not through <c>SubscriptionPaymentLink</c> and the activation
/// processor. That path expires the subscription when a charge is abandoned, and reads one link per
/// subscription as the signup's -- an abandoned top-up must not end a subscription, and a second
/// link would be mistaken for the signup. The payment is raised under the purchase's own charge key,
/// which is what ties it back here from the webhook, a retry, or the sweep.
/// <para>
/// Credits are granted only on the provider's word: a signed webhook, or a read of the session
/// applied through the same path one would take. Never on the buyer's browser coming back.
/// </para>
/// </remarks>
public sealed partial class SubscriptionCreditService
{
    /// <summary>The audit and failure codes a checkout purchase can end with.</summary>
    private const string CheckoutExpiredCode = "subscription_credits_checkout_expired";
    private const string CheckoutDeclinedCode = "subscription_credits_checkout_declined";

    private bool CheckoutAvailable => _paymentService is not null && _currency is not null;

    /// <summary>
    /// The organization scope a checkout opens under: the one readiness resolved the frozen provider
    /// from. Mirrors SubscriptionCheckoutService.ResolveProviderOrganizationId -- see its remarks for
    /// why a null scope on an account pinned to a provider row is kept, not coerced.
    /// </summary>
    private static string? CheckoutProviderOrganizationId(BillingAccount account, SubscriptionDetail subscription) =>
        account.ProviderId is null
            ? account.ProviderOrganizationId ?? subscription.OrganizationId
            : account.ProviderOrganizationId;

    private static SubscriptionOperationResult<CreditPurchaseResponse> CheckoutInProgress(
        SubscriptionCreditPurchase open,
        string correlationId)
    {
        // Same shape as the signup's pending-checkout conflict, so a client handles both alike.
        var details = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["purchaseId"] = [open.ItemId]
        };

        if (open.CheckoutUrl is { Length: > 0 } url)
        {
            details["checkoutUrl"] = [url];
        }

        return SubscriptionOperationResult<CreditPurchaseResponse>.Failure(
            PaymentFailureKind.Conflict,
            "subscription_credits_checkout_in_progress",
            "Another credit purchase on this subscription is waiting to be paid. Finish it at its " +
                "checkout, or wait for it to expire, before starting another.",
            correlationId,
            details);
    }

    /// <summary>
    /// Opens the hosted checkout for a purchase recorded at Pending, and moves it to AwaitingPayment.
    /// </summary>
    private async Task<SubscriptionOperationResult<CreditPurchaseResponse>> StartCheckoutAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        SubscriptionCreditPurchase purchase,
        BillingAccount account,
        string? reservedDiscountId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.ProviderName) ||
            !_currency!.TryConvertBack(purchase.TotalMinor, purchase.CurrencyCode, out var amount))
        {
            // Nothing left the building, so nothing to unwind but the purchase and its code.
            return await AbandonCheckoutAsync(
                purchase, reservedDiscountId, PaymentFailureKind.Unavailable,
                "subscription_billing_account_provider_unavailable",
                "This subscription's billing account cannot take a payment. Contact support.",
                correlationId, cancellationToken);
        }

        var started = await _paymentService!.MakePaymentAsync(
            new MakePaymentRequest
            {
                // The billing account's frozen provider and row, as signup checkout uses -- never
                // re-resolved from the merchant profile.
                ProviderName = account.ProviderName,
                ExpectedProviderId = account.ProviderId,
                OrganizationId = purchase.ProviderOrganizationId,
                CustomerOrganizationId = subscription.OrganizationId,
                // The card this saves is the subscriber's, and the billing account adopts it by
                // that organization, not by the merchant scope the money is taken under.
                PaymentMethodOwnerOrganizationId = subscription.OrganizationId,
                CustomerEmail = account.BillingEmail is { Length: > 0 } email ? email : null,
                Amount = amount,
                CurrencyCode = purchase.CurrencyCode,
                OrderId = purchase.OrderId,
                Description = $"{Format(purchase.Quantity)} {CreditLabel(meter)}",
                SavePaymentMethod = true,
                // Later top-ups charge this card with the Subscription model
                // (RecurringChargeBillingGateway), so it must be saved under it. On Adyen this is
                // also what stores the card unconditionally rather than behind a consent box.
                RecurringModel = PaymentConstants.SubscriptionRecurringModel,
                CheckoutExpiresInMinutes = Math.Clamp(_options.CurrentValue.CreditCheckoutSessionMinutes, 30, 24 * 60)
            },
            // The purchase's own charge key: a retry finds this payment instead of opening a
            // second session, and the sweep can find it if this request dies before recording it.
            SubscriptionConstants.CreditPurchaseChargeKeyFor(purchase.ItemId),
            correlationId,
            cancellationToken);

        if (!started.IsSuccess || started.Payment is null)
        {
            if (!SettledFailureKinds.Contains(started.FailureKind))
            {
                // A session may yet exist. Leave the purchase Pending: the sweep looks the payment
                // up by its key and either adopts it or, finding none, fails the purchase.
                _logger.LogWarning(
                    "Credit checkout start unanswered; left for the recovery sweep PurchaseHash={PurchaseHash} Reason={Reason}",
                    PaymentLogValue.Hash(purchase.ItemId),
                    PaymentLogValue.Label(started.ErrorCode));

                return Failure<CreditPurchaseResponse>(
                    started.FailureKind,
                    "subscription_credits_checkout_unresolved",
                    "The payment provider did not open the checkout. Retry with the same idempotency " +
                        "key to see whether it did.",
                    correlationId);
            }

            return await AbandonCheckoutAsync(
                purchase, reservedDiscountId, started.FailureKind, started.ErrorCode, started.ErrorMessage,
                correlationId, cancellationToken);
        }

        // Defense in depth, as signup checkout does: a session opened under a different provider
        // row than the account was pinned to is never handed to the buyer.
        if (account.ProviderId is { Length: > 0 } expectedProviderId &&
            started.Payment.ResolvedProviderId is { Length: > 0 } resolvedProviderId &&
            !string.Equals(resolvedProviderId, expectedProviderId, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Credit checkout resolved a different provider configuration than the billing " +
                "account was pinned to; refusing it PurchaseHash={PurchaseHash}",
                PaymentLogValue.Hash(purchase.ItemId));

            return await AbandonCheckoutAsync(
                purchase, reservedDiscountId, PaymentFailureKind.Unavailable,
                "subscription_payment_provider_scope_mismatch",
                "The payment provider could not be reached under the merchant configuration this " +
                    "subscription was pinned to.",
                correlationId, cancellationToken);
        }

        OpenCheckout(purchase, started.Payment.PaymentDetailId, started.Payment.RedirectUrl, started.Payment.ExpiresAtUtc);
        await _purchases.TryAdvanceAsync(purchase, purchase.Version, cancellationToken);

        _logger.LogInformation(
            "Credit checkout opened PurchaseHash={PurchaseHash} PaymentId={PaymentId}",
            PaymentLogValue.Hash(purchase.ItemId),
            PaymentLogValue.Id(purchase.PaymentId ?? string.Empty));

        // An immediate decision (a replayed payment that already settled) is honoured at once.
        await SettleCheckoutAsync(purchase, correlationId, cancellationToken);

        return SubscriptionOperationResult<CreditPurchaseResponse>.Success(
            PurchaseResponse(purchase, await GrantOfAsync(purchase, cancellationToken), replayed: false),
            correlationId);
    }

    private void OpenCheckout(
        SubscriptionCreditPurchase purchase,
        string paymentId,
        string? checkoutUrl,
        DateTime? expiresAtUtc)
    {
        var now = Now();
        purchase.PaymentId = paymentId;
        purchase.CheckoutUrl = checkoutUrl;
        // The provider's own expiry when it reported one; otherwise the lifetime that was asked for.
        purchase.CheckoutExpiresAtUtc = expiresAtUtc ??
            purchase.CreatedAtUtc.AddMinutes(Math.Clamp(_options.CurrentValue.CreditCheckoutSessionMinutes, 30, 24 * 60));
        purchase.Status = CreditPurchaseStatus.AwaitingPayment;
        purchase.LastUpdatedAtUtc = now;
    }

    private async Task<SubscriptionOperationResult<CreditPurchaseResponse>> AbandonCheckoutAsync(
        SubscriptionCreditPurchase purchase,
        string? reservedDiscountId,
        PaymentFailureKind kind,
        string? code,
        string? message,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (reservedDiscountId is not null)
        {
            await _redemptions.TryReleaseAsync(
                purchase.TenantId, reservedDiscountId, purchase.ItemId, Now(), cancellationToken);
        }

        await FailAsync(purchase, code, message, cancellationToken);

        _logger.LogWarning(
            "Credit checkout could not be opened PurchaseHash={PurchaseHash} Reason={Reason}",
            PaymentLogValue.Hash(purchase.ItemId),
            PaymentLogValue.Label(code ?? "unknown"));

        return Failure<CreditPurchaseResponse>(
            kind,
            code ?? "subscription_credits_checkout_failed",
            message ?? "The checkout could not be opened.",
            correlationId);
    }

    /// <summary>
    /// Asks the provider about an open checkout when the buyer comes back, and settles on the
    /// answer. Stripe only: Adyen has no session read, so there only the webhook or sweep decides.
    /// </summary>
    private async Task ReadCheckoutOnReturnAsync(
        SubscriptionCreditPurchase purchase,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (await SettleCheckoutAsync(purchase, correlationId, cancellationToken) ||
            _reconciler is null ||
            purchase.PaymentId is not { Length: > 0 } paymentId)
        {
            return;
        }

        if (await _reconciler.TryReconcileAsync(purchase.TenantId, paymentId, cancellationToken))
        {
            await SettleCheckoutAsync(purchase, correlationId, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<int> SettleCheckoutsForPaymentsAsync(
        string tenantId,
        IReadOnlyCollection<string> paymentIds,
        CancellationToken cancellationToken)
    {
        if (paymentIds is null || paymentIds.Count == 0)
        {
            return 0;
        }

        var settled = 0;

        foreach (var paymentId in paymentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            // A point read. Most payments are not a credit checkout's, and find nothing.
            var purchase = await _purchases.FindCheckoutByPaymentAsync(tenantId, paymentId, cancellationToken);

            if (purchase is not null &&
                await SettleCheckoutAsync(purchase, $"credit-checkout-{Guid.NewGuid():N}", cancellationToken))
            {
                settled++;
            }
        }

        return settled;
    }

    /// <summary>
    /// Carries a decided payment across to its checkout purchase: grants on a confirmation, fails on
    /// a decline, and leaves an undecided one alone. Safe to repeat and to run from several places
    /// at once -- the move off AwaitingPayment is a compare-and-set.
    /// </summary>
    /// <returns>True when this call changed the purchase's outcome.</returns>
    private async Task<bool> SettleCheckoutAsync(
        SubscriptionCreditPurchase purchase,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (purchase.PaymentPath != CreditPaymentPath.Checkout ||
            purchase.PaymentId is not { Length: > 0 } paymentId ||
            purchase.Status is not (CreditPurchaseStatus.AwaitingPayment or CreditPurchaseStatus.Failed))
        {
            return false;
        }

        var payment = await _payments.GetByIdAsync(purchase.TenantId, paymentId, cancellationToken);

        if (payment is null)
        {
            return false;
        }

        var now = Now();

        // Confirmed means an accepting status and a webhook that said so -- the activation
        // processor's rule. A session read is applied through the webhook path, so it counts too.
        var confirmed = ChargedStatuses.Contains(payment.PaymentStatus, StringComparer.Ordinal) &&
                        payment.WebhookConfirmedAtUtc is not null;

        if (!confirmed)
        {
            if (purchase.Status != CreditPurchaseStatus.AwaitingPayment ||
                !DeclinedStatuses.Contains(payment.PaymentStatus, StringComparer.Ordinal))
            {
                return false;
            }

            // Expired is how Stripe's session read reports a checkout nobody paid at.
            var expired = string.Equals(payment.PaymentStatus, PaymentStatuses.Expired, StringComparison.Ordinal);
            await FailCheckoutAsync(
                purchase,
                expired ? CheckoutExpiredCode : CheckoutDeclinedCode,
                expired ? "The checkout expired before it was paid." : "The payment was not completed.",
                now,
                cancellationToken);
            return true;
        }

        var subscription = await _subscriptions.GetByIdAsync(purchase.TenantId, purchase.SubscriptionId, cancellationToken);
        var outcome = await GrantPaidCheckoutAsync(purchase, payment, subscription, now, correlationId, cancellationToken);

        if (outcome && subscription is not null)
        {
            await AdoptCardAsync(purchase, subscription, payment, cancellationToken);
        }

        return outcome;
    }

    /// <summary>Grants a confirmed checkout, or hands it to an operator where granting would be wrong.</summary>
    private async Task<bool> GrantPaidCheckoutAsync(
        SubscriptionCreditPurchase purchase,
        PaymentDetail payment,
        SubscriptionDetail? subscription,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Paid after it was given up on (a late webhook): the code may have gone to another
        // purchase and the buyer may have bought again, so an operator decides, not this.
        if (purchase.Status == CreditPurchaseStatus.Failed)
        {
            return await NeedsAttentionAsync(purchase, "subscription_credits_paid_after_failure",
                "The checkout was paid after the purchase had been failed.", now, cancellationToken);
        }

        if (!_currency!.TryConvert((decimal)payment.Amount, payment.CurrencyCode, out var paidMinor) ||
            paidMinor != purchase.TotalMinor ||
            !string.Equals(payment.CurrencyCode, purchase.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return await NeedsAttentionAsync(purchase, "subscription_credits_amount_mismatch",
                "The amount paid does not match the quote.", now, cancellationToken);
        }

        // Credits granted into an ended subscription are forfeited the moment they land (PR #695).
        if (subscription is null ||
            subscription.Status is SubscriptionStatus.Canceled or SubscriptionStatus.IncompleteExpired)
        {
            return await NeedsAttentionAsync(purchase, "subscription_credits_subscription_ended",
                "The subscription ended before the checkout was paid.", now, cancellationToken);
        }

        // The expiry was fixed when quoted. Credits that would arrive already expired are not what
        // was paid for.
        if (purchase.ExpiresAtUtc is { } expiresAt && expiresAt <= now)
        {
            return await NeedsAttentionAsync(purchase, "subscription_credits_expired_before_payment",
                "The credits' usage window ended before the checkout was paid.", now, cancellationToken);
        }

        purchase.Status = CreditPurchaseStatus.Charged;
        purchase.LastUpdatedAtUtc = now;

        if (!await _purchases.TryAdvanceAsync(purchase, purchase.Version, cancellationToken))
        {
            // Another pass (webhook, retry or sweep) got here first and owns the grant.
            return false;
        }

        await FinalizeAsync(
            subscription,
            purchase,
            purchase.DiscountId,
            new SubscriptionContext(purchase.TenantId, purchase.OrganizationId, "credit-checkout", purchase.BuyerUserId),
            correlationId,
            cancellationToken);

        _logger.LogInformation(
            "Credit checkout paid and granted PurchaseHash={PurchaseHash}",
            PaymentLogValue.Hash(purchase.ItemId));

        return true;
    }

    private async Task<bool> NeedsAttentionAsync(
        SubscriptionCreditPurchase purchase,
        string code,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        purchase.Status = CreditPurchaseStatus.NeedsAttention;
        purchase.FailureCode = code;
        purchase.FailureReason = reason;
        purchase.LastUpdatedAtUtc = now;

        if (!await _purchases.TryAdvanceAsync(purchase, purchase.Version, cancellationToken))
        {
            return false;
        }

        // Money was taken and nothing granted for it. Never silent.
        _logger.LogError(
            "A paid credit checkout was not granted and needs an operator PurchaseHash={PurchaseHash} " +
            "PaymentHash={PaymentHash} Reason={Reason}",
            PaymentLogValue.Hash(purchase.ItemId),
            PaymentLogValue.Hash(purchase.PaymentId ?? string.Empty),
            PaymentLogValue.Label(code));

        return true;
    }

    private async Task FailCheckoutAsync(
        SubscriptionCreditPurchase purchase,
        string code,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (purchase.DiscountId is { } discountId)
        {
            await _redemptions.TryReleaseAsync(purchase.TenantId, discountId, purchase.ItemId, now, cancellationToken);
        }

        await FailAsync(purchase, code, reason, cancellationToken);
    }

    /// <summary>
    /// Offers the card a paid checkout saved to the billing account, as its default only if it has
    /// none. A card the provider has not reported yet leaves <see cref="SubscriptionCreditPurchase.CardAdoptedAtUtc"/>
    /// unset for the sweep to try again; it never holds the grant back.
    /// </summary>
    private async Task AdoptCardAsync(
        SubscriptionCreditPurchase purchase,
        SubscriptionDetail subscription,
        PaymentDetail payment,
        CancellationToken cancellationToken)
    {
        if (_cardAdopter is null ||
            await _cardAdopter.AdoptAsync(subscription, payment, onlyIfNoDefault: true, cancellationToken) is not null)
        {
            return;
        }

        purchase.CardAdoptedAtUtc = Now();
        purchase.LastUpdatedAtUtc = purchase.CardAdoptedAtUtc.Value;
        await _purchases.TryAdvanceAsync(purchase, purchase.Version, cancellationToken);
    }

    /// <summary>The sweep's pass over a checkout purchase still at Pending or AwaitingPayment.</summary>
    private async Task<bool> RecoverCheckoutAsync(
        SubscriptionCreditPurchase purchase,
        DateTime now,
        SubscriptionOptions options,
        CancellationToken cancellationToken)
    {
        var correlationId = $"credit-checkout-recovery-{Guid.NewGuid():N}";

        if (purchase.Status == CreditPurchaseStatus.Pending)
        {
            // The request that recorded it died, or never heard back from the provider. The
            // payment, if one was raised, is under the purchase's own charge key.
            var payment = await FindChargeAsync(purchase, cancellationToken);

            if (payment is null)
            {
                await FailCheckoutAsync(purchase, "subscription_credits_checkout_not_started",
                    "The checkout was interrupted before it opened.", now, cancellationToken);
                return true;
            }

            OpenCheckout(
                purchase,
                payment.ItemId,
                payment.RedirectUrl,
                payment.ExpirationDate == default ? null : payment.ExpirationDate);

            if (!await _purchases.TryAdvanceAsync(purchase, purchase.Version, cancellationToken))
            {
                return false;
            }
        }

        if (await SettleCheckoutAsync(purchase, correlationId, cancellationToken))
        {
            return true;
        }

        if (_reconciler is not null &&
            purchase.PaymentId is { Length: > 0 } paymentId &&
            await _reconciler.TryReconcileAsync(purchase.TenantId, paymentId, cancellationToken) &&
            await SettleCheckoutAsync(purchase, correlationId, cancellationToken))
        {
            return true;
        }

        var deadline = (purchase.CheckoutExpiresAtUtc ?? purchase.CreatedAtUtc.AddMinutes(options.CreditCheckoutSessionMinutes))
            .AddMinutes(Math.Max(0, options.CreditCheckoutExpiryGraceMinutes));

        if (now > deadline)
        {
            // Past the session's life with no decision: nobody can pay there any more, so the code
            // goes back. A payment that still turns up later is sent to an operator, not granted.
            await FailCheckoutAsync(purchase, CheckoutExpiredCode,
                "The checkout expired before it was paid.", now, cancellationToken);
            return true;
        }

        // Still open. Touched so it does not hold the front of every batch.
        purchase.LastUpdatedAtUtc = now;
        await _purchases.TryAdvanceAsync(purchase, purchase.Version, cancellationToken);
        return false;
    }

    /// <summary>Retries adoption for paid checkouts whose card the provider reported late.</summary>
    private async Task AdoptPendingCardsAsync(
        string tenantId,
        DateTime now,
        SubscriptionOptions options,
        CancellationToken cancellationToken)
    {
        if (_cardAdopter is null)
        {
            return;
        }

        var due = await _purchases.ListCardAdoptionDueAsync(
            tenantId,
            now.AddHours(-Math.Max(1, options.CreditPurchaseAlertAfterHours)),
            now.AddSeconds(-Math.Max(30, options.CreditPurchaseRecoveryAfterSeconds)),
            Math.Max(1, options.CreditMaintenanceBatchSize),
            cancellationToken);

        foreach (var purchase in due)
        {
            var payment = purchase.PaymentId is { Length: > 0 } paymentId
                ? await _payments.GetByIdAsync(tenantId, paymentId, cancellationToken)
                : null;
            var subscription = await _subscriptions.GetByIdAsync(tenantId, purchase.SubscriptionId, cancellationToken);

            if (payment is not null && subscription is not null)
            {
                await AdoptCardAsync(purchase, subscription, payment, cancellationToken);
            }

            if (purchase.CardAdoptedAtUtc is null)
            {
                purchase.LastUpdatedAtUtc = now;
                await _purchases.TryAdvanceAsync(purchase, purchase.Version, cancellationToken);
            }
        }
    }

    private async Task<SubscriptionCreditGrant?> GrantOfAsync(
        SubscriptionCreditPurchase purchase,
        CancellationToken cancellationToken) =>
        purchase.GrantId is { } grantId
            ? await _grants.GetAsync(purchase.TenantId, grantId, cancellationToken)
            : null;
}
