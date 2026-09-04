using Subscription.DomainService.Entities;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// What a mid-period plan change costs, or credits, right now.
/// </summary>
/// <remarks>
/// Pure and static, like <see cref="SubscriptionAmountCalculator"/> — the instant is always a
/// parameter. Both the old and new amounts go through the exact same gross, discount and tax math
/// a renewal uses (<see cref="SubscriptionAmountCalculator.GrossAmountMinor"/>,
/// <see cref="SubscriptionAmountCalculator.ApplyDiscount"/>,
/// <see cref="SubscriptionAmountCalculator.TaxBreakdownFor"/>), just against two different
/// price/quantity pairs. The discount is the subscriber's, not the plan's, so it applies to both
/// sides identically — but tax is each side's own price's rate and mode, since a plan change can
/// move the subscriber to a differently-taxed price, or to one that quotes tax the other way
/// round.
/// </remarks>
public static class SubscriptionProrationCalculator
{
    /// <param name="targetPlan">
    /// The plan being priced on the new side. The current plan for a quantity change, a different
    /// one for a plan change — its volume bands and combination policy are what the new side is
    /// held to, since those are what the subscriber is moving onto.
    /// </param>
    public static ProrationOutcome Calculate(
        SubscriptionDetail subscription,
        PlanSnapshot targetPlan,
        PriceSnapshot targetPrice,
        IReadOnlyList<SubscriptionQuantityItem> targetQuantityItems,
        DateTime nowUtc,
        DateTime targetPeriodStartUtc,
        DateTime targetPeriodEndUtc,
        BillingDayFraction targetFraction = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(targetPlan);
        ArgumentNullException.ThrowIfNull(targetPrice);
        ArgumentNullException.ThrowIfNull(targetQuantityItems);

        var totalTicks = (subscription.CurrentPeriodEndUtc - subscription.CurrentPeriodStartUtc).Ticks;

        if (totalTicks <= 0)
        {
            // A malformed period grants nothing either way rather than dividing by zero or
            // guessing — the caller should not have reached here with one.
            return new ProrationOutcome(0, subscription.CreditBalanceMinor);
        }

        var remainingTicks = Math.Clamp(
            (subscription.CurrentPeriodEndUtc - nowUtc).Ticks,
            0,
            totalTicks);

        // Both sides through the same band-aware path a renewal uses, so a quantity change is
        // priced at the band its quantity actually selects rather than at the flat unit amount.
        var oldDiscounted = SubscriptionAmountCalculator.DiscountedAmountMinor(
            subscription.Plan,
            subscription.Discount,
            subscription.Price,
            subscription.QuantityItems,
            subscription.DiscountPeriodsApplied,
            nowUtc);

        // A partial period on a calendar-aligned yearly target is charged from the monthly price
        // that annual price was linked to, exactly as a fresh signup on it would be. Prorating the
        // annual amount by days instead would bill a week at roughly twelve times its worth.
        var newPrice = targetPrice;
        var newQuantityItems = targetQuantityItems;

        if (targetFraction.IsPartial &&
            CalendarBillingAlignment.TryStubBasis(
                targetPrice,
                targetQuantityItems,
                out var stubPrice,
                out var stubQuantityItems))
        {
            newPrice = stubPrice;
            newQuantityItems = stubQuantityItems;
        }

        // The target's own fraction, where it has one. A move onto a calendar-aligned price buys
        // the days from here to the first of next month, and those are counted as calendar dates
        // rather than as elapsed time — the same 7/31 a fresh signup on the same day would pay.
        var newDiscounted = SubscriptionAmountCalculator.DiscountedAmountMinor(
            targetPlan,
            subscription.Discount,
            newPrice,
            newQuantityItems,
            subscription.DiscountPeriodsApplied,
            nowUtc,
            targetFraction);

        // Each side settled at its own price's rate *and* mode, before the two are netted against
        // each other. A plan change can move a subscriber from a tax-exclusive price to an inclusive
        // one, and netting the configured amounts first would compare a number that contains tax
        // with one that does not.
        var oldTaxInclusive = SubscriptionAmountCalculator.TaxBreakdownFor(
            oldDiscounted.AmountMinor,
            subscription.Price.TaxRateBasisPoints,
            subscription.Price.TaxMode).TotalAmountMinor;
        var newTaxInclusive = SubscriptionAmountCalculator.TaxBreakdownFor(
            newDiscounted.AmountMinor,
            newPrice.TaxRateBasisPoints,
            newPrice.TaxMode).TotalAmountMinor;

        // What recurs, as distinct from what is being bought. Everything above prices the period
        // this settlement covers, and for a calendar-aligned target that period is a stub: priced
        // by day fraction, and for a yearly target from the linked monthly basis rather than the
        // annual amount at all. Neither is what the subscriber pays from the next boundary on, so
        // the whole period is priced separately — against the *original* target price, not the
        // stub-swapped one, and with no fraction.
        //
        // Only when there is a fraction to undo. Prorate returns an amount untouched once
        // coveredDays reaches totalDays (see CalendarBillingAlignment.Prorate), so for a whole
        // period — every anniversary target, and every change landing on the first —
        // newTaxInclusive already *is* the full period, and reusing it keeps those quotes
        // bit-identical rather than merely equal by inspection.
        var targetFullPeriodTotalMinor = targetFraction.IsPartial
            ? FullPeriod(subscription, targetPlan, targetPrice, targetQuantityItems, nowUtc)
            : newTaxInclusive;

        var oldRemainingValue = Prorate(oldTaxInclusive, remainingTicks, totalTicks);
        var targetTotalTicks = (targetPeriodEndUtc - targetPeriodStartUtc).Ticks;
        var targetRemainingTicks = Math.Clamp(
            (targetPeriodEndUtc - nowUtc).Ticks,
            0,
            Math.Max(0, targetTotalTicks));

        // A day-counted target is already exactly the period being bought — it runs from now to
        // the next boundary — so scaling it again by the time left in it would prorate it twice.
        //
        // Every calendar-priced target, not only the partial ones. A change landing on the first
        // buys a whole month, and letting the clock scale it would charge a subscriber who moved
        // at noon less than one who signed up fresh at noon for the identical month.
        var newRemainingCost = targetFraction.IsCalendarPriced
            ? newTaxInclusive
            : targetTotalTicks <= 0
                ? 0
                : Prorate(newTaxInclusive, targetRemainingTicks, targetTotalTicks);

        var rawDelta = newRemainingCost - oldRemainingValue;

        // Reported, not recomputed later. Every figure below was needed to reach the charge, and an
        // invoice for a settlement cannot be explained from the charge alone: "CHF 41.30" is the
        // remainder of a subtraction between two prorated periods, and a subscriber asking why is
        // asking about the two sides, not the remainder.
        var outgoing = new ProrationSide(
            oldDiscounted.GrossAmountMinor,
            oldDiscounted.BuiltInDiscountMinor,
            oldDiscounted.PromotionalDiscountMinor,
            oldTaxInclusive - oldDiscounted.AmountMinor,
            oldTaxInclusive,
            oldRemainingValue);
        var target = new ProrationSide(
            newDiscounted.GrossAmountMinor,
            newDiscounted.BuiltInDiscountMinor,
            newDiscounted.PromotionalDiscountMinor,
            newTaxInclusive - newDiscounted.AmountMinor,
            newTaxInclusive,
            newRemainingCost);

        var settled = SettleRawDelta(rawDelta, subscription.CreditBalanceMinor);
        var breakdown = new ProrationBreakdown(
            outgoing, target, settled.CreditConsumedMinor, settled.NetSettlementMinor);

        return new ProrationOutcome(
            settled.ChargeMinor,
            settled.NewCreditBalanceMinor,
            breakdown,
            targetFullPeriodTotalMinor);
    }

    /// <summary>
    /// A whole period at a plan and price, tax included, with no day fraction applied — what a
    /// renewal on these terms will charge.
    /// </summary>
    /// <remarks>
    /// Priced through the same pair every other full period in this module goes through, so it
    /// cannot drift from what a renewal actually charges: the subscriber's own discount at their
    /// own period index, then the price's own tax rate and mode.
    /// <para>
    /// Deliberately not <see cref="SubscriptionAmountCalculator.PeriodAmountMinor"/>, which
    /// subtracts <see cref="SubscriptionDetail.CreditBalanceMinor"/>. The settlement already spends
    /// that same balance in <see cref="SettleRawDelta"/>, so a subscriber holding credit would see
    /// it deducted twice — once off the charge, and again off the recurring price they were quoted.
    /// </para>
    /// </remarks>
    private static long FullPeriod(
        SubscriptionDetail subscription,
        PlanSnapshot plan,
        PriceSnapshot price,
        IReadOnlyList<SubscriptionQuantityItem> quantityItems,
        DateTime nowUtc)
    {
        var discounted = SubscriptionAmountCalculator.DiscountedAmountMinor(
            plan,
            subscription.Discount,
            price,
            quantityItems,
            subscription.DiscountPeriodsApplied,
            nowUtc);

        return SubscriptionAmountCalculator.TaxBreakdownFor(
            discounted.AmountMinor, price.TaxRateBasisPoints, price.TaxMode).TotalAmountMinor;
    }

    /// <param name="currentAnnual">
    /// The year already frozen on the subscription — bought at signup and, since this method may
    /// only be called while it is prepaid, already paid for in full. Read verbatim for the
    /// outgoing annual side rather than recomputed, because it is exactly what was charged and
    /// cannot drift from a live recalculation the way an on-the-fly figure could.
    /// </param>
    /// <param name="stubFraction">
    /// How much of a calendar month the days between now and the boundary cover — the same
    /// <see cref="Repositories.SubscriptionPlanSchedule.FeePeriodFraction"/> a fresh schedule
    /// resolved at this instant already carries. Shared between the outgoing and target stub
    /// sides: both run to the identical boundary, since this method is only ever called once the
    /// caller has confirmed the target keeps the subscriber's cadence and alignment, so only the
    /// per-day <em>rate</em> differs between them, never the days themselves.
    /// </param>
    /// <remarks>
    /// Two settlements at once, computed and credited together rather than one after the other:
    /// the remaining value of the stub at its own monthly-equivalent rate, and the difference
    /// between what the paid year cost and what it costs on the new terms. The stub side excludes
    /// the subscriber's promotional code on both its outgoing and target sides, mirroring exactly
    /// how the stub was priced at signup — a code belongs to the year, never to the days before
    /// it, and repricing the stub as if it had one would credit a discount that was never actually
    /// spent on it, understating what is still owed for days already lived on the plan being left.
    /// The annual side carries the code on both sides, for the identical reason in reverse.
    /// <para>
    /// Credit is spent once, against the combined total, never against the stub and the annual
    /// side separately — spending it twice would let a settlement worth less on one side consume
    /// the same balance twice.
    /// </para>
    /// </remarks>
    public static OpeningStubUpgradeOutcome CalculateOpeningStubUpgrade(
        SubscriptionDetail subscription,
        PlanSnapshot targetPlan,
        PriceSnapshot targetPrice,
        IReadOnlyList<SubscriptionQuantityItem> targetQuantityItems,
        PendingAnnualPeriod currentAnnual,
        DateTime nowUtc,
        BillingDayFraction stubFraction)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(targetPlan);
        ArgumentNullException.ThrowIfNull(targetPrice);
        ArgumentNullException.ThrowIfNull(targetQuantityItems);
        ArgumentNullException.ThrowIfNull(currentAnnual);

        var outgoingStub = StubSide(
            subscription.Plan, subscription.Price, subscription.QuantityItems, nowUtc, stubFraction);
        var targetStub = StubSide(targetPlan, targetPrice, targetQuantityItems, nowUtc, stubFraction);

        // The frozen figures verbatim — this is what was actually charged, or promised, for the
        // year. ProratedValueMinor is the whole amount: the year has not started, so none of it is
        // "used" yet, and the entire figure is the baseline the target year is compared against.
        var outgoingAnnual = new ProrationSide(
            currentAnnual.GrossAmountMinor,
            currentAnnual.BuiltInDiscountMinor,
            currentAnnual.PromotionalDiscountMinor,
            currentAnnual.TaxAmountMinor,
            currentAnnual.AmountMinor,
            currentAnnual.AmountMinor);

        // Priced exactly as SubscriptionCreationService.BuildPendingAnnualPeriod prices the year at
        // signup: the whole period, the subscriber's own discount included, at the target price's
        // own tax rate and mode.
        //
        // At the index that priced the year being replaced, not at the subscription's current one.
        // A prepaid year has already spent its promotional period — the activation that collected
        // it counted one, and SubscriptionRenewalService deliberately does not count a second when
        // the year opens ("a prepaid year reduced it once already"). Repricing the replacement at
        // the current index would therefore treat a one-period promotion as exhausted and quote the
        // year undiscounted, so the upgrade would charge the plan difference *plus* repayment of a
        // discount the subscriber has already been granted for this very period.
        //
        // Conditioned on the frozen year's own DiscountApplied rather than stepped back
        // unconditionally: when no promotion reduced this year, nothing was counted for it, and
        // stepping the index back anyway would revive a period the promotion never actually spent
        // on this year — handing out a discount that was never bought.
        var annualDiscountPeriodsApplied = currentAnnual.DiscountApplied
            ? Math.Max(0, subscription.DiscountPeriodsApplied - 1)
            : subscription.DiscountPeriodsApplied;

        var targetAnnualCharge = SubscriptionAmountCalculator.DiscountedAmountMinor(
            targetPlan,
            subscription.Discount,
            targetPrice,
            targetQuantityItems,
            annualDiscountPeriodsApplied,
            nowUtc);
        var targetAnnualTax = SubscriptionAmountCalculator.TaxBreakdownFor(
            targetAnnualCharge.AmountMinor, targetPrice.TaxRateBasisPoints, targetPrice.TaxMode);
        var targetAnnual = new ProrationSide(
            targetAnnualCharge.GrossAmountMinor,
            targetAnnualCharge.BuiltInDiscountMinor,
            targetAnnualCharge.PromotionalDiscountMinor,
            targetAnnualTax.TaxAmountMinor,
            targetAnnualTax.TotalAmountMinor,
            targetAnnualTax.TotalAmountMinor);

        var stubRawDelta = targetStub.ProratedValueMinor - outgoingStub.ProratedValueMinor;
        var annualRawDelta = targetAnnual.ProratedValueMinor - outgoingAnnual.ProratedValueMinor;
        var combinedRawDelta = stubRawDelta + annualRawDelta;

        var settled = SettleRawDelta(combinedRawDelta, subscription.CreditBalanceMinor);

        // Each side's own breakdown carries its raw, pre-credit figure for the invoice to explain
        // — "the stub came to X, the year came to Y" — and reports no credit of its own, since
        // credit was never spent against either side in isolation. Only the combination above
        // reflects what was actually charged and what the balance actually became.
        var stubBreakdown = new ProrationBreakdown(outgoingStub, targetStub, 0, stubRawDelta);
        var annualBreakdown = new ProrationBreakdown(outgoingAnnual, targetAnnual, 0, annualRawDelta);

        return new OpeningStubUpgradeOutcome(
            settled.ChargeMinor,
            settled.NewCreditBalanceMinor,
            settled.CreditConsumedMinor,
            combinedRawDelta,
            settled.NetSettlementMinor,
            stubBreakdown,
            annualBreakdown,
            targetAnnualCharge.DiscountApplied);
    }

    /// <summary>
    /// One side of the stub component of an opening-stub upgrade: a plan and price's monthly
    /// stub-basis rate, discounted with no promotional code, for the fraction of a month the stub
    /// still covers.
    /// </summary>
    private static ProrationSide StubSide(
        PlanSnapshot plan,
        PriceSnapshot price,
        IReadOnlyList<SubscriptionQuantityItem> quantityItems,
        DateTime nowUtc,
        BillingDayFraction stubFraction)
    {
        var (basisPrice, basisQuantityItems) = CalendarBillingAlignment.TryStubBasis(
            price, quantityItems, out var stubPrice, out var stubQuantityItems)
            ? (stubPrice, stubQuantityItems)
            : (price, quantityItems);

        // No discount here — see this method's caller. Built-in and volume reductions still apply,
        // since those belong to the price rather than to the subscriber's code.
        var discounted = SubscriptionAmountCalculator.DiscountedAmountMinor(
            plan, null, basisPrice, basisQuantityItems, 0, nowUtc, stubFraction);
        var tax = SubscriptionAmountCalculator.TaxBreakdownFor(
            discounted.AmountMinor, basisPrice.TaxRateBasisPoints, basisPrice.TaxMode);

        return new ProrationSide(
            discounted.GrossAmountMinor,
            discounted.BuiltInDiscountMinor,
            discounted.PromotionalDiscountMinor,
            tax.TaxAmountMinor,
            tax.TotalAmountMinor,
            tax.TotalAmountMinor);
    }

    /// <summary>
    /// The one money rule every settlement in this module answers to: the balance can only ever
    /// fall.
    /// </summary>
    /// <remarks>
    /// Credit spent bringing a charge down is real and the remainder must persist, but a
    /// settlement worth less than what it replaced must not hand the difference back as new
    /// credit: a downgrade is not refunded, and neither is an increase that reaches a cheaper
    /// volume band. Both are worth exactly what they cost — nothing — and banking value for either
    /// is a refund under another name.
    /// <para>
    /// Factored out rather than left inline in <see cref="Calculate"/>, which is where this rule
    /// first existed, so that <see cref="CalculateOpeningStubUpgrade"/> — settling a combined
    /// stub-and-annual delta rather than a single period's — inherits the identical rule instead
    /// of a second copy of it that could quietly drift from the first.
    /// </para>
    /// </remarks>
    private static (long ChargeMinor, long NewCreditBalanceMinor, long CreditConsumedMinor, long NetSettlementMinor)
        SettleRawDelta(long rawDelta, long creditBalanceMinor)
    {
        var netAfterCredit = rawDelta - creditBalanceMinor;
        var creditConsumedMinor = Math.Clamp(rawDelta, 0, Math.Max(0, creditBalanceMinor));

        return netAfterCredit > 0
            ? (netAfterCredit, 0, creditConsumedMinor, netAfterCredit)
            : (0, Math.Min(creditBalanceMinor, -netAfterCredit), creditConsumedMinor, netAfterCredit);
    }

    /// <summary>
    /// Scales an amount by the fraction of the period remaining, in exact integer arithmetic.
    /// </summary>
    /// <remarks>
    /// <c>amount * remainingTicks</c> overflows a <see cref="long"/> long before the numbers
    /// involved look unreasonable — a year-long period is already ~3×10^14 ticks. Widening to
    /// <see cref="Int128"/> for the multiplication keeps this exact rather than reaching for a
    /// floating-point ratio, which the rest of this module's money arithmetic deliberately never
    /// does.
    /// </remarks>
    private static long Prorate(long amountMinor, long remainingTicks, long totalTicks) =>
        (long)((Int128)amountMinor * remainingTicks / totalTicks);
}

/// <param name="ChargeMinor">What to charge now. Zero when the change is fully covered by credit.</param>
/// <param name="NewCreditBalanceMinor">
/// The credit balance to write back. Never more than the balance the subscription already held:
/// a settlement may consume credit, in full or in part, but never creates any — see the clamp in
/// <see cref="SubscriptionProrationCalculator.Calculate"/>.
/// </param>
/// <param name="Breakdown">
/// The two sides the charge came from, for the payment record. Default when the period was malformed
/// and nothing could be prorated.
/// </param>
/// <param name="TargetFullPeriodTotalMinor">
/// What a whole period at the target costs, tax included — what recurs from the next boundary on,
/// as opposed to <see cref="ProrationBreakdown.Target"/>'s
/// <see cref="ProrationSide.PeriodTotalMinor"/>, which is the period this settlement actually
/// prices.
/// </param>
/// <remarks>
/// <see cref="TargetFullPeriodTotalMinor"/> is deliberately here rather than on
/// <see cref="ProrationSide"/>. A side is what reaches storage and the financial documents —
/// <c>SettlementCharge.SideOf</c>, <c>SubscriptionSettlementBreakdown</c>, the invoice HTML and its
/// React mirror all read one — and widening it would rewrite records already written. An outcome is
/// returned by <see cref="SubscriptionProrationCalculator.Calculate"/> and never serialised.
/// </remarks>
public readonly record struct ProrationOutcome(
    long ChargeMinor,
    long NewCreditBalanceMinor,
    ProrationBreakdown Breakdown = default,
    long TargetFullPeriodTotalMinor = 0);

/// <summary>
/// One side of a settlement: what a period costs, and how much of that this instant is worth.
/// </summary>
/// <param name="GrossAmountMinor">The period before any reduction.</param>
/// <param name="BuiltInDiscountMinor">What the price's automatic discount and the volume band took off.</param>
/// <param name="PromotionalDiscountMinor">What a promotional code took off after that.</param>
/// <param name="TaxAmountMinor">Tax on what was left, at this side's own rate and mode.</param>
/// <param name="PeriodTotalMinor">The whole period, tax included — gross less discounts, plus tax.</param>
/// <param name="ProratedValueMinor">
/// The part of that this settlement counts: unused time on the outgoing side, remaining time on the
/// target side.
/// </param>
public readonly record struct ProrationSide(
    long GrossAmountMinor,
    long BuiltInDiscountMinor,
    long PromotionalDiscountMinor,
    long TaxAmountMinor,
    long PeriodTotalMinor,
    long ProratedValueMinor);

/// <summary>
/// Both sides of a settlement and what closed the gap between them.
/// </summary>
/// <remarks>
/// A settlement is a subtraction, so a single gross-and-discount pair cannot describe it: the
/// subscriber is leaving one priced period part-way through and joining another, and both have their
/// own discounts and their own tax. <see cref="NetSettlementMinor"/> is target prorated value less
/// outgoing unused value less credit — negative when a downgrade banks credit rather than charging.
/// </remarks>
public readonly record struct ProrationBreakdown(
    ProrationSide Outgoing,
    ProrationSide Target,
    long CreditConsumedMinor,
    long NetSettlementMinor);

/// <summary>
/// What an upgrade taken during a calendar-aligned yearly subscription's opening stub costs, once
/// that stub's own year has already been paid for.
/// </summary>
/// <param name="ChargeMinor">What is left to collect after the combined settlement spends credit.</param>
/// <param name="NewCreditBalanceMinor">The balance afterward — never higher than it was before.</param>
/// <param name="CreditConsumedMinor">What the balance actually paid for, of the combined total.</param>
/// <param name="RawSettlementMinor">
/// The combined stub-and-annual delta before credit pays any of it. Used for classification —
/// whether this change is worth more than what it replaces is a property of the change, not of how
/// much credit happens to be lying around — mirroring how the ordinary immediate-vs-scheduled path
/// classifies on <see cref="ProrationBreakdown.NetSettlementMinor"/> rather than the post-credit
/// charge.
/// </param>
/// <param name="NetSettlementMinor">The combined delta after credit, for the invoice's final total.</param>
/// <param name="Stub">
/// The opening stub's own settlement: its remaining value at the outgoing plan's stub-basis rate,
/// against its remaining value at the target's.
/// </param>
/// <param name="Annual">The prepaid year's own settlement: what was paid, against what it costs on the target's terms.</param>
/// <param name="TargetAnnualDiscountApplied">
/// Whether the subscriber's promotional code actually reduced the target annual amount, to carry
/// onto the replacement <see cref="PendingAnnualPeriod.DiscountApplied"/> — read later, when the
/// year opens, to decide whether to count it against the code's remaining duration.
/// </param>
public readonly record struct OpeningStubUpgradeOutcome(
    long ChargeMinor,
    long NewCreditBalanceMinor,
    long CreditConsumedMinor,
    long RawSettlementMinor,
    long NetSettlementMinor,
    ProrationBreakdown Stub,
    ProrationBreakdown Annual,
    bool TargetAnnualDiscountApplied);
