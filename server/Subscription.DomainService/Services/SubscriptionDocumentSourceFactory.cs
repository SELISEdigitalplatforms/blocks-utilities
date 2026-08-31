using Payment.DomainService.Entities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Freezes what a financial event was about, at the moment it happened.
/// </summary>
/// <remarks>
/// Pure and static, because it runs inside money paths that cannot afford a dependency or a
/// round trip, and because what it produces has to be reproducible from its inputs alone: the same
/// transition composed twice must yield the same source key, or the append that is supposed to be
/// idempotent stops being.
/// <para>
/// The counterpart of the issuer. This says what the money was for; the issuer says what the money
/// was. Splitting them that way is what lets the document be written long after the event without
/// describing it in terms of a plan the subscriber has since moved off.
/// </para>
/// </remarks>
public static class SubscriptionDocumentSourceFactory
{
    /// <summary>
    /// The obligation a settled charge leaves behind.
    /// </summary>
    /// <remarks>
    /// Records no amounts. What was taken is on the payment, which is the only version of the figures
    /// the bank agrees with; a second copy here would be free to disagree with it.
    /// </remarks>
    public static SubscriptionDocumentSource ForCharge(
        SubscriptionDetail subscription,
        string paymentDetailId,
        SubscriptionChargeKind chargeKind,
        string? periodKey,
        FinancialDocumentPerson? initiatedBy,
        DateTime occurredAtUtc,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var charge = new SubscriptionChargeReference(
            subscription.ItemId,
            chargeKind,
            periodKey);

        return new SubscriptionDocumentSource
        {
            SourceKey = FinancialDocumentSourceKey.ForPayment(paymentDetailId),
            PaymentDetailId = paymentDetailId,
            DocumentType = FinancialDocumentType.Invoice,
            ChargeKind = chargeKind,
            PeriodKey = periodKey,
            CurrencyCode = subscription.CurrencyCode,
            Subject = SubjectOf(subscription),
            QuantityItems = Frozen(subscription.QuantityItems),
            // Resolved here, against the schedule in force now. A renewal's period cannot be worked
            // out later: a plan change rebuilds the fee schedule, and the same period key against the
            // new one names different dates.
            Period = PeriodFor(subscription, charge),
            InitiatedBy = initiatedBy,
            OccurredAtUtc = occurredAtUtc,
            CorrelationId = correlationId
        };
    }

    /// <summary>The obligation a trial start leaves behind: a document stating terms, not a charge.</summary>
    public static SubscriptionDocumentSource? ForTrial(
        SubscriptionDetail subscription,
        FinancialDocumentPerson? initiatedBy,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (subscription.Trial is not { } trial)
        {
            return null;
        }

        return new SubscriptionDocumentSource
        {
            SourceKey = FinancialDocumentSourceKey.ForTrial(subscription.ItemId, trial.StartsAtUtc),
            DocumentType = FinancialDocumentType.TrialInvoice,
            ChargeKind = SubscriptionChargeKind.Initial,
            CurrencyCode = subscription.CurrencyCode,
            Subject = SubjectOf(subscription),
            QuantityItems = Frozen(subscription.QuantityItems),
            Period = new FinancialDocumentPeriod
            {
                StartUtc = trial.StartsAtUtc,
                EndUtc = trial.EndsAtUtc,
                TimeZoneId = subscription.FeeSchedule.TimeZoneId
            },
            Trial = new FinancialDocumentTrial
            {
                StartsAtUtc = trial.StartsAtUtc,
                EndsAtUtc = trial.EndsAtUtc,
                RequiresPaymentMethod = trial.RequiresPaymentMethod,
                FirstBillingAtUtc = subscription.NextFeeBillingAtUtc
            },
            InitiatedBy = initiatedBy,
            OccurredAtUtc = trial.StartsAtUtc,
            CorrelationId = correlationId
        };
    }

    /// <summary>
    /// Who acted, from a user id alone.
    /// </summary>
    /// <remarks>
    /// The name is left for the issuer to resolve from the billing contact recorded against that user,
    /// because a worker settling a change hours later has no session to read a name from. Null for no
    /// user, which is what makes the document say the system acted rather than naming whoever last
    /// touched the subscription.
    /// </remarks>
    public static FinancialDocumentPerson? ActorOf(string? userId) =>
        userId is { Length: > 0 } present
            ? new FinancialDocumentPerson { UserId = present }
            : null;

    /// <summary>Who acted, with the name and address their identity provider gave at the time.</summary>
    public static FinancialDocumentPerson? ActorOf(string? userId, string? name, string? email) =>
        userId is { Length: > 0 } || name is { Length: > 0 }
            ? new FinancialDocumentPerson
            {
                UserId = userId,
                Name = name ?? userId ?? string.Empty,
                Email = email
            }
            : null;

    /// <summary>
    /// Copies the units, item by item.
    /// </summary>
    /// <remarks>
    /// A copy of the list is not a copy of the units. The items are reference types, so a snapshot
    /// holding the same instances would follow the subscription: a seat count changed later in the
    /// same request would silently rewrite what the obligation says was charged for, which is the one
    /// thing it exists to pin down.
    /// </remarks>
    private static List<SubscriptionQuantityItem> Frozen(
        IEnumerable<SubscriptionQuantityItem> items) =>
    [
        .. items.Select(item => new SubscriptionQuantityItem
        {
            ItemKey = item.ItemKey,
            UnitLabel = item.UnitLabel,
            Quantity = item.Quantity,
            UnitAmountMinor = item.UnitAmountMinor
        })
    ];

    public static FinancialDocumentSubject SubjectOf(SubscriptionDetail subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return new FinancialDocumentSubject
        {
            PlanCode = subscription.Plan.Code,
            PlanName = subscription.Plan.DisplayName,
            PriceId = subscription.Price.PriceId,
            Interval = subscription.Price.Interval,
            IntervalCount = subscription.Price.IntervalCount,
            UnitAmountMinor = subscription.Price.UnitAmountMinor
        };
    }

    /// <summary>
    /// The service period a charge covered, from the period key where there is one.
    /// </summary>
    /// <remarks>
    /// The key rather than the subscription's current period, because the two disagree whenever the
    /// document is issued after the subscription has moved on — normally a matter of seconds, but a
    /// renewal that catches up several periods after an outage settles them one after another and
    /// each has to say which one it covered.
    /// <para>
    /// The subscription's own snapshotted fee schedule turns that start into a start and an end, so
    /// the boundaries are the ones the subscriber was actually billed on rather than a month added to
    /// a date.
    /// </para>
    /// </remarks>
    public static FinancialDocumentPeriod PeriodFor(
        SubscriptionDetail subscription,
        SubscriptionChargeReference charge)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var timeZoneId = subscription.FeeSchedule.TimeZoneId;

        if (charge.Kind == SubscriptionChargeKind.Usage)
        {
            // The usage window, which is a different cadence from the fee period on purpose: an
            // annual plan still meters monthly.
            if (PeriodKey.TryDecodeStart(charge.PeriodKey, out var usageStart) &&
                BillingPeriodCalculator.TryGetPeriod(
                    subscription.UsageSchedule,
                    usageStart,
                    out var usagePeriod))
            {
                return new FinancialDocumentPeriod
                {
                    StartUtc = usagePeriod.StartUtc,
                    EndUtc = usagePeriod.EndUtc,
                    TimeZoneId = timeZoneId,
                    PeriodKey = charge.PeriodKey ?? string.Empty
                };
            }

            return new FinancialDocumentPeriod
            {
                StartUtc = subscription.CurrentUsagePeriodStartUtc,
                EndUtc = subscription.CurrentUsagePeriodEndUtc,
                TimeZoneId = timeZoneId,
                PeriodKey = charge.PeriodKey ?? string.Empty
            };
        }

        if (charge.Kind == SubscriptionChargeKind.Renewal &&
            PeriodKey.TryDecodeStart(charge.PeriodKey, out var start) &&
            BillingPeriodCalculator.TryGetPeriod(subscription.FeeSchedule, start, out var period))
        {
            return new FinancialDocumentPeriod
            {
                StartUtc = period.StartUtc,
                EndUtc = period.EndUtc,
                TimeZoneId = timeZoneId,
                PeriodKey = period.Key
            };
        }

        // The initial charge and both settlements: the period the subscription is in, which for the
        // initial charge is exactly the one it paid for and for a settlement is the one the change
        // was prorated against.
        return new FinancialDocumentPeriod
        {
            StartUtc = subscription.CurrentPeriodStartUtc,
            EndUtc = subscription.CurrentPeriodEndUtc,
            TimeZoneId = timeZoneId,
            PeriodKey = charge.PeriodKey ?? string.Empty,
            IsProrated = charge.Kind == SubscriptionChargeKind.Initial &&
                subscription.InitialChargeProrated,
            ProratedDays = charge.Kind == SubscriptionChargeKind.Initial
                ? subscription.ProrationDays
                : null,
            ProratedTotalDays = charge.Kind == SubscriptionChargeKind.Initial
                ? subscription.ProrationTotalDays
                : null
        };
    }
}
