using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Reads back what <see cref="SubscriptionConstants"/> wrote into a charge's order id.
/// </summary>
/// <remarks>
/// The reader beside the writer, in the same assembly and over the same constants, because the two
/// drifting apart is a defect this module has already had: both settlement kinds once shared one
/// segment, so plan-change charges reported themselves as renewals and handed a reservation id to
/// whatever expected a period key.
/// <para>
/// The order id is a label and a classifier. It is never the dedupe — that is the idempotency key —
/// so parsing it can be made stricter or looser without any risk to what a retry finds.
/// </para>
/// </remarks>
public static class SubscriptionOrderId
{
    /// <param name="orderId">
    /// An order id from a payment. Anything not written by this module — a payment from another
    /// product in the same tenant — resolves to <see cref="SubscriptionChargeKind.Unknown"/> with no
    /// subscription, which is what keeps a foreign payment from being invoiced as a subscription.
    /// </param>
    public static SubscriptionChargeReference Parse(string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId) ||
            !orderId.StartsWith(SubscriptionConstants.OrderIdPrefix, StringComparison.Ordinal))
        {
            return new SubscriptionChargeReference(null, SubscriptionChargeKind.Unknown, null);
        }

        var value = orderId[SubscriptionConstants.OrderIdPrefix.Length..];
        var separator = value.IndexOf(':', StringComparison.Ordinal);

        if (separator < 0)
        {
            // The bare form, which only the first charge uses: hosted checkout, before there is a
            // period to name.
            return value.Length == 0
                ? new SubscriptionChargeReference(null, SubscriptionChargeKind.Unknown, null)
                : new SubscriptionChargeReference(value, SubscriptionChargeKind.Initial, null);
        }

        if (separator == 0 || separator == value.Length - 1)
        {
            return new SubscriptionChargeReference(null, SubscriptionChargeKind.Unknown, null);
        }

        var subscriptionId = value[..separator];
        var suffix = value[(separator + 1)..];

        if (Names(SubscriptionConstants.PlanChangeSegment, suffix) ||
            Names(SubscriptionConstants.LegacyPlanChangeSegment, suffix))
        {
            return new SubscriptionChargeReference(
                subscriptionId,
                SubscriptionChargeKind.PlanChange,
                null);
        }

        // The legacy spelling covers both kinds — it is what they shared before they were told
        // apart — so a plan change charged back then reads as a quantity change. The alternative is
        // guessing, and guessing about somebody's invoice is worse than being coarse about it.
        if (Names(SubscriptionConstants.QuantitySegment, suffix) ||
            Names(SubscriptionConstants.LegacySettlementSegment, suffix))
        {
            return new SubscriptionChargeReference(
                subscriptionId,
                SubscriptionChargeKind.QuantityChange,
                null);
        }

        if (Names(SubscriptionConstants.UsageSegment, suffix))
        {
            var periodKey = suffix[(SubscriptionConstants.UsageSegment.Length + 1)..];

            return new SubscriptionChargeReference(
                subscriptionId,
                SubscriptionChargeKind.Usage,
                string.IsNullOrWhiteSpace(periodKey) ? null : periodKey);
        }

        // Everything else is a renewal, whose suffix is its period key. Deliberately the fallback
        // rather than a named segment: renewals predate every other suffix and their ids carry no
        // marker, so recognising them by exhaustion is the only reading that stays correct for the
        // ids already written.
        return new SubscriptionChargeReference(
            subscriptionId,
            SubscriptionChargeKind.Renewal,
            suffix);
    }

    private static bool Names(string segment, string suffix) =>
        suffix.StartsWith($"{segment}:", StringComparison.Ordinal);
}

/// <summary>What a charge's order id says about it.</summary>
/// <param name="SubscriptionId">Null when the id was not written by this module.</param>
/// <param name="PeriodKey">
/// The billing or usage period, where the kind has one. Null on a settlement, whose suffix is a
/// reservation id rather than a period — the mistake that made this a shared type.
/// </param>
public readonly record struct SubscriptionChargeReference(
    string? SubscriptionId,
    SubscriptionChargeKind Kind,
    string? PeriodKey);
