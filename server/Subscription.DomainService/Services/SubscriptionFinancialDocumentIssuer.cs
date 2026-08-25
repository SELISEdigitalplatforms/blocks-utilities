using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Issues financial documents from what the money path already recorded.
/// </summary>
/// <remarks>
/// Derives rather than being told. A settled charge already carries everything a document needs —
/// which subscription, which period, what came off before tax, what tax, what credit — and the order
/// id says what kind of charge it was. So one method covers all six charge paths, and none of them
/// has to know how a document is composed.
/// <para>
/// That matters for more than tidiness: the recovery sweep can issue a document for a payment nobody
/// scheduled, because it reads the same payment and reaches the same answer. A design where each
/// money path passed its own figures in would leave recovery with nothing to read.
/// </para>
/// <para>
/// Nothing here moves money, releases a reservation or changes a subscription. It is a bookkeeping
/// step scheduled <em>after</em> the money and state transitions commit, and its worst failure is a
/// missing document, which the sweep fixes.
/// </para>
/// </remarks>
public sealed class SubscriptionFinancialDocumentIssuer : ISubscriptionFinancialDocumentIssuer
{
    private static readonly string[] SettledStatuses =
    [
        PaymentStatuses.Captured,
        PaymentStatuses.PartiallyRefunded,
        PaymentStatuses.Refunded
    ];

    private readonly ISubscriptionFinancialDocumentRepository _documents;
    private readonly IFinancialDocumentNumberAllocator _numbers;
    private readonly ISubscriptionBillingProfileRepository _profiles;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPaymentRepository _payments;
    private readonly ISubscriptionInvoiceHistoryRepository _settledCharges;
    private readonly ICurrencyMinorUnitResolver _currency;
    private readonly IOptions<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionFinancialDocumentIssuer> _logger;
    private readonly ISubscriptionWorkScheduler? _scheduler;
    private readonly TimeProvider _time;

    public SubscriptionFinancialDocumentIssuer(
        ISubscriptionFinancialDocumentRepository documents,
        IFinancialDocumentNumberAllocator numbers,
        ISubscriptionBillingProfileRepository profiles,
        ISubscriptionRepository subscriptions,
        IPaymentRepository payments,
        ISubscriptionInvoiceHistoryRepository settledCharges,
        ICurrencyMinorUnitResolver currency,
        IOptions<SubscriptionOptions> options,
        ILogger<SubscriptionFinancialDocumentIssuer> logger,
        ISubscriptionWorkScheduler? scheduler = null,
        TimeProvider? time = null)
    {
        _documents = documents;
        _numbers = numbers;
        _profiles = profiles;
        _subscriptions = subscriptions;
        _payments = payments;
        _settledCharges = settledCharges;
        _currency = currency;
        _options = options;
        _logger = logger;
        _scheduler = scheduler;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionFinancialDocument?> IssueForPaymentAsync(
        string tenantId,
        string paymentDetailId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var payment = await _payments.GetByIdAsync(tenantId, paymentDetailId, cancellationToken);
        if (payment is null || !SettledStatuses.Contains(payment.PaymentStatus))
        {
            // Nothing settled, so nothing to invoice. A failed, abandoned or still-pending attempt
            // is not a document, and issuing one for it would put revenue in the ledger that the
            // bank never saw.
            return null;
        }

        var charge = SubscriptionOrderId.Parse(payment.OrderId);
        if (charge is not { SubscriptionId: { Length: > 0 } subscriptionId } ||
            charge.Kind == SubscriptionChargeKind.Unknown)
        {
            // Some other product's payment in the same tenant. Read-only and ignored.
            return null;
        }

        var subscription = await _subscriptions.GetByIdAsync(
            tenantId,
            subscriptionId,
            cancellationToken);

        if (subscription is null)
        {
            _logger.LogWarning(
                "A settled subscription charge names a subscription that no longer exists, so no " +
                "document was issued PaymentHash={PaymentHash} ChargeKind={ChargeKind}",
                PaymentLogValue.Hash(paymentDetailId),
                charge.Kind);

            return null;
        }

        var amounts = AmountsFor(payment, subscription, charge);
        if (amounts.TotalMinor <= 0 && payment.SubscriptionSettlement is null)
        {
            // A zero charge that is not a settlement never reached the provider, so there is no
            // money for a document to describe. A settlement of zero is different: the two sides
            // cancelled out and the subscriber is entitled to see why.
            return null;
        }

        return await ComposeAndIssueAsync(
            subscription,
            FinancialDocumentType.Invoice,
            FinancialDocumentSourceKey.ForPayment(paymentDetailId),
            payment.PaymentDate == default ? _time.GetUtcNow().UtcDateTime : payment.PaymentDate,
            amounts,
            LinesFor(subscription, charge, amounts),
            PeriodFor(subscription, charge),
            correlationId,
            paymentDetailId: paymentDetailId,
            settlement: payment.SubscriptionSettlement,
            initiatedByUserId: payment.UserId,
            settlementReservationId: null,
            cancellationToken: cancellationToken);
    }

    public async Task<int> IssuePendingAsync(
        string tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var since = _time.GetUtcNow().UtcDateTime
            .AddHours(-Math.Max(1, options.DocumentIssueLookbackHours));

        var settled = await _settledCharges.ListSettledSinceAsync(
            tenantId,
            since,
            Math.Max(1, options.DocumentDeliveryBatchSize),
            cancellationToken);

        var issued = 0;

        foreach (var charge in settled)
        {
            // Asked before issuing rather than counting what came back, because almost every charge
            // in the window already has its document and the interesting number is how many did not.
            // Issuing is idempotent either way; this only keeps the count — and the warning below —
            // honest about what recovery actually recovered.
            var existing = await _documents.FindBySourceKeyAsync(
                tenantId,
                FinancialDocumentSourceKey.ForPayment(charge.PaymentDetailId),
                cancellationToken);

            if (existing is not null)
            {
                continue;
            }

            if (await IssueForPaymentAsync(
                    tenantId,
                    charge.PaymentDetailId,
                    correlationId,
                    cancellationToken) is not null)
            {
                issued++;
            }
        }

        issued += await IssuePendingCreditNotesAsync(
            tenantId,
            since,
            Math.Max(1, options.DocumentDeliveryBatchSize),
            correlationId,
            cancellationToken);

        if (issued > 0)
        {
            _logger.LogWarning(
                "A recovery pass issued financial documents that the money path had not " +
                "IssuedCount={IssuedCount} TenantHash={TenantHash}",
                issued,
                PaymentLogValue.Hash(tenantId));
        }

        return issued;
    }

    /// <summary>
    /// Issues the credit notes for refunds that have confirmed since the window opened.
    /// </summary>
    /// <remarks>
    /// The only route a refund reaches this module by. A refund confirms inside the payment module,
    /// which must never depend on subscriptions, so nothing there can announce it — the subscription
    /// side has to come and look. Polling for it is the price of keeping that dependency
    /// one-directional, and a small one: the window is hours and the query is indexed.
    /// </remarks>
    private async Task<int> IssuePendingCreditNotesAsync(
        string tenantId,
        DateTime since,
        int limit,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var refunded = await _settledCharges.ListRefundedSinceAsync(
            tenantId,
            since,
            limit,
            cancellationToken);

        var issued = 0;

        foreach (var charge in refunded)
        {
            foreach (var refundId in charge.SucceededRefundIds)
            {
                var existing = await _documents.FindBySourceKeyAsync(
                    tenantId,
                    FinancialDocumentSourceKey.ForRefund(refundId),
                    cancellationToken);

                if (existing is not null)
                {
                    continue;
                }

                if (await IssueRefundCreditNoteAsync(
                        tenantId,
                        charge.PaymentDetailId,
                        refundId,
                        correlationId,
                        cancellationToken) is not null)
                {
                    issued++;
                }
            }
        }

        return issued;
    }

    public async Task<SubscriptionFinancialDocument?> IssueTrialInvoiceAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (subscription.Trial is not { } trial)
        {
            return null;
        }

        var granted = subscription.QuantityItems
            .Select(item => new FinancialDocumentLine
            {
                Description = $"{item.UnitLabel} (granted for the trial)",
                Quantity = item.Quantity,
                UnitAmountMinor = 0,
                AmountMinor = 0,
                ItemKey = item.ItemKey
            })
            .ToList();

        granted.Insert(0, new FinancialDocumentLine
        {
            Description = $"{subscription.Plan.DisplayName} trial",
            AmountMinor = 0
        });

        return await ComposeAndIssueAsync(
            subscription,
            FinancialDocumentType.TrialInvoice,
            FinancialDocumentSourceKey.ForTrial(subscription.ItemId, trial.StartsAtUtc),
            trial.StartsAtUtc,
            // Zero throughout, deliberately explicit rather than an absent block: a trial invoice
            // states that nothing was charged, which is different from not saying what was charged.
            new FinancialDocumentAmounts
            {
                TaxRateBasisPoints = subscription.Price.TaxRateBasisPoints,
                TaxMode = SubscriptionTaxPresentation.Describe(subscription.Price)
            },
            granted,
            new FinancialDocumentPeriod
            {
                StartUtc = trial.StartsAtUtc,
                EndUtc = trial.EndsAtUtc,
                TimeZoneId = subscription.FeeSchedule.TimeZoneId
            },
            correlationId,
            paymentDetailId: null,
            settlement: null,
            initiatedByUserId: null,
            settlementReservationId: null,
            trial: new FinancialDocumentTrial
            {
                StartsAtUtc = trial.StartsAtUtc,
                EndsAtUtc = trial.EndsAtUtc,
                RequiresPaymentMethod = trial.RequiresPaymentMethod,
                FirstBillingAtUtc = subscription.NextFeeBillingAtUtc
            },
            cancellationToken: cancellationToken);
    }

    public async Task<SubscriptionFinancialDocument?> IssueRefundCreditNoteAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var payment = await _payments.GetByIdAsync(tenantId, paymentDetailId, cancellationToken);

        var refund = payment?.Refunds
            .FirstOrDefault(item => string.Equals(
                item.RefundId,
                refundId,
                StringComparison.Ordinal));

        if (payment is null ||
            refund is null ||
            !string.Equals(
                refund.Status,
                PaymentRefundStatuses.Succeeded,
                StringComparison.Ordinal))
        {
            // Only a confirmed refund credits anything. One that is submitted, failed or reversed
            // has returned no money, and a credit note for it would be a promise the bank did not
            // keep.
            return null;
        }

        // The invoice being adjusted, found by the payment it was issued for. Its absence is not a
        // reason to skip the credit note — a refund of a pre-migration charge still has to be
        // documented — but a credit note that can link to its invoice always does.
        var original = await _documents.FindBySourceKeyAsync(
            tenantId,
            FinancialDocumentSourceKey.ForPayment(paymentDetailId),
            cancellationToken);

        var charge = SubscriptionOrderId.Parse(payment.OrderId);
        if (charge.SubscriptionId is not { Length: > 0 } subscriptionId)
        {
            return null;
        }

        var subscription = await _subscriptions.GetByIdAsync(
            tenantId,
            subscriptionId,
            cancellationToken);

        if (subscription is null)
        {
            return null;
        }

        if (!_currency.TryConvert(refund.Amount, refund.CurrencyCode, out var refundedMinor) ||
            refundedMinor <= 0)
        {
            return null;
        }

        var amounts = original is not null
            ? ReverseProportionally(original.Amounts, refundedMinor)
            : new FinancialDocumentAmounts
            {
                GrossSubtotalMinor = refundedMinor,
                NetSubtotalMinor = refundedMinor,
                TotalMinor = refundedMinor
            };

        var document = await ComposeAndIssueAsync(
            subscription,
            FinancialDocumentType.CreditNote,
            FinancialDocumentSourceKey.ForRefund(refundId),
            refund.CompletedAtUtc ?? refund.UpdatedAtUtc,
            amounts,
            [
                new FinancialDocumentLine
                {
                    Description = original is not null
                        ? $"Refund of {original.DocumentNumber}"
                        : $"Refund of {subscription.Plan.DisplayName}",
                    AmountMinor = amounts.TotalMinor
                }
            ],
            original?.Period ?? PeriodFor(subscription, charge),
            correlationId,
            paymentDetailId: paymentDetailId,
            settlement: null,
            initiatedByUserId: null,
            settlementReservationId: null,
            originalDocument: original,
            refundId: refundId,
            cancellationToken: cancellationToken);

        if (document is not null && original is not null)
        {
            // A summary of the credit notes, kept on the invoice so a list can render a badge
            // without joining. Derived from the payment's own refunded total rather than from this
            // one refund, so several partial refunds converge on "refunded" rather than each one
            // reporting "partially".
            await _documents.TrySetRefundStatusAsync(
                tenantId,
                original.ItemId,
                payment.RefundedAmount >= payment.PreciseAmount
                    ? FinancialDocumentStatus.Refunded
                    : FinancialDocumentStatus.PartiallyRefunded,
                cancellationToken);
        }

        return document;
    }

    public async Task<SubscriptionFinancialDocument?> IssueDowngradeCreditNoteAsync(
        SubscriptionDetail subscription,
        string changeReference,
        long creditedMinor,
        SubscriptionSettlementBreakdown? settlement,
        string? initiatedByUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (creditedMinor <= 0 || string.IsNullOrWhiteSpace(changeReference))
        {
            return null;
        }

        var amounts = new FinancialDocumentAmounts
        {
            GrossSubtotalMinor = creditedMinor,
            NetSubtotalMinor = creditedMinor,
            TotalMinor = creditedMinor,
            TaxRateBasisPoints = subscription.Price.TaxRateBasisPoints,
            TaxMode = SubscriptionTaxPresentation.Describe(subscription.Price)
        };

        return await ComposeAndIssueAsync(
            subscription,
            FinancialDocumentType.CreditNote,
            FinancialDocumentSourceKey.ForDowngradeCredit(subscription.ItemId, changeReference),
            _time.GetUtcNow().UtcDateTime,
            amounts,
            [
                new FinancialDocumentLine
                {
                    Description =
                        $"Unused time credited on downgrade to {subscription.Plan.DisplayName}",
                    AmountMinor = creditedMinor
                }
            ],
            new FinancialDocumentPeriod
            {
                StartUtc = subscription.CurrentPeriodStartUtc,
                EndUtc = subscription.CurrentPeriodEndUtc,
                TimeZoneId = subscription.FeeSchedule.TimeZoneId
            },
            correlationId,
            paymentDetailId: null,
            settlement: settlement,
            initiatedByUserId: initiatedByUserId,
            settlementReservationId: changeReference,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The one place a document is actually built, numbered, inserted and queued for delivery.
    /// </summary>
    /// <remarks>
    /// Allocate, then insert. A number taken by a call that then loses the duplicate race is
    /// abandoned rather than reused, which is why the sequence may have gaps — the correct trade,
    /// because a gap is a question an auditor can answer and a reused number is not.
    /// </remarks>
    private async Task<SubscriptionFinancialDocument?> ComposeAndIssueAsync(
        SubscriptionDetail subscription,
        FinancialDocumentType documentType,
        string sourceKey,
        DateTime issuedAtUtc,
        FinancialDocumentAmounts amounts,
        List<FinancialDocumentLine> lines,
        FinancialDocumentPeriod period,
        string correlationId,
        string? paymentDetailId,
        SubscriptionSettlementBreakdown? settlement,
        string? initiatedByUserId,
        string? settlementReservationId,
        CancellationToken cancellationToken,
        FinancialDocumentTrial? trial = null,
        SubscriptionFinancialDocument? originalDocument = null,
        string? refundId = null)
    {
        // Asked first, so a document that already exists costs one indexed read rather than a
        // number allocation it would have to throw away. The unique index is still what guarantees
        // the outcome; this only keeps the sequence tidy in the common replay.
        var existing = await _documents.FindBySourceKeyAsync(
            subscription.TenantId,
            sourceKey,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var profile = await _profiles.GetAsync(
            subscription.TenantId,
            subscription.OrganizationId,
            cancellationToken);

        var issuedAt = issuedAtUtc == default
            ? _time.GetUtcNow().UtcDateTime
            : issuedAtUtc.ToUniversalTime();

        var number = await _numbers.AllocateAsync(
            subscription.TenantId,
            documentType,
            issuedAt.Year,
            cancellationToken);

        var document = new SubscriptionFinancialDocument
        {
            DocumentNumber = number,
            DocumentType = documentType,
            IssuedAtUtc = issuedAt,
            TenantId = subscription.TenantId,
            OrganizationId = subscription.OrganizationId,
            SubscriptionId = subscription.ItemId,
            PaymentDetailId = paymentDetailId,
            RefundId = refundId,
            SettlementReservationId = settlementReservationId,
            OriginalDocumentId = originalDocument?.ItemId,
            OriginalDocumentNumber = originalDocument?.DocumentNumber,
            SourceKey = sourceKey,
            CurrencyCode = subscription.CurrencyCode,
            Merchant = MerchantSnapshot(),
            Subscriber = SubscriberSnapshot(subscription, profile),
            BillingContact = ContactSnapshot(profile),
            InitiatedBy = InitiatorSnapshot(profile, initiatedByUserId),
            Subject = new FinancialDocumentSubject
            {
                PlanCode = subscription.Plan.Code,
                PlanName = subscription.Plan.DisplayName,
                PriceId = subscription.Price.PriceId,
                Interval = subscription.Price.Interval,
                IntervalCount = subscription.Price.IntervalCount,
                UnitAmountMinor = subscription.Price.UnitAmountMinor
            },
            Trial = trial,
            Period = WithLocalDates(period),
            Amounts = amounts,
            Settlement = settlement,
            Lines = lines,
            CorrelationId = correlationId
        };

        var outcome = await _documents.InsertAsync(document, cancellationToken);

        if (!outcome.Inserted)
        {
            // Somebody else issued it between the read above and this insert. Theirs stands, and
            // theirs is the one already queued for delivery — queueing again would risk a second
            // email for the same document.
            return outcome.Document;
        }

        _logger.LogInformation(
            "Financial document issued DocumentNumber={DocumentNumber} " +
            "DocumentType={DocumentType} TenantHash={TenantHash} " +
            "SubscriptionHash={SubscriptionHash} TotalMinor={TotalMinor}",
            PaymentLogValue.Label(number),
            documentType,
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            amounts.TotalMinor);

        await ScheduleDeliveryAsync(outcome.Document, cancellationToken);

        return outcome.Document;
    }

    /// <summary>
    /// Announces the PDF and the email, and never lets that failure reach the caller.
    /// </summary>
    /// <remarks>
    /// By the time this runs the money has moved and the document is issued and numbered. A
    /// scheduling write in another database that fails costs a later delivery, which the sweep finds;
    /// throwing here would make a settled invoice look like unfinished work and invite a retry that
    /// can only re-read what already exists.
    /// </remarks>
    private async Task ScheduleDeliveryAsync(
        SubscriptionFinancialDocument document,
        CancellationToken cancellationToken)
    {
        if (_scheduler is null)
        {
            return;
        }

        try
        {
            await _scheduler.TryScheduleAsync(
                SubscriptionWorkType.FinancialDocumentDelivery,
                document.TenantId,
                $"document:{document.ItemId}",
                _time.GetUtcNow().UtcDateTime,
                document.CorrelationId,
                document.ItemId,
                document.OrganizationId,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "A financial document was issued but its delivery could not be scheduled; the " +
                "sweep will pick it up DocumentNumber={DocumentNumber}",
                PaymentLogValue.Label(document.DocumentNumber));
        }
    }

    /// <summary>
    /// What the charge was made of, from the strongest source available.
    /// </summary>
    /// <remarks>
    /// Three sources, in order of how directly they know. A renewal, settlement or overage records
    /// its own breakdown on the payment as it charges, so that is read verbatim. The initial charge
    /// goes out through hosted checkout, which composes no invoice and records no breakdown — but
    /// every input is snapshotted on the subscription and the amount was frozen there, so the same
    /// calculator that priced it can be asked again and its answer checked against the frozen figure.
    /// Anything older than either is reported as a single gross line, which is all that can honestly
    /// be said about it.
    /// </remarks>
    private FinancialDocumentAmounts AmountsFor(
        PaymentDetail payment,
        SubscriptionDetail subscription,
        SubscriptionChargeReference charge)
    {
        if (payment.SubscriptionNetAmountMinor is { } net)
        {
            var (automatic, quantity) = BuiltInDiscountAttribution.Split(
                payment.SubscriptionBuiltInDiscountMinor ?? 0,
                payment.SubscriptionAutomaticDiscountBasisPoints,
                payment.SubscriptionQuantityDiscountBasisPoints,
                payment.SubscriptionDiscountCombination);

            var tax = payment.SubscriptionTaxAmountMinor ?? 0;
            var credit = payment.SubscriptionCreditAmountMinor ?? 0;

            return new FinancialDocumentAmounts
            {
                GrossSubtotalMinor = payment.SubscriptionGrossAmountMinor ?? net,
                AutomaticDiscountMinor = automatic,
                QuantityDiscountMinor = quantity,
                PromotionalDiscountMinor = payment.SubscriptionPromotionalDiscountMinor ?? 0,
                NetSubtotalMinor = net,
                TaxRateBasisPoints = payment.SubscriptionTaxRateBasisPoints,
                TaxMode = payment.SubscriptionTaxMode,
                TaxAmountMinor = tax,
                CreditAppliedMinor = credit,
                TotalMinor = net + tax - credit,
                AutomaticDiscountBasisPoints = payment.SubscriptionAutomaticDiscountBasisPoints,
                QuantityDiscountBasisPoints = payment.SubscriptionQuantityDiscountBasisPoints,
                DiscountCombination = payment.SubscriptionDiscountCombination,
                PromotionCode = subscription.Discount?.Code
            };
        }

        if (charge.Kind == SubscriptionChargeKind.Initial &&
            RecomposeInitialCharge(subscription) is { } initial)
        {
            return initial;
        }

        return SingleGrossLine(payment);
    }

    /// <summary>
    /// The initial charge's breakdown, recomputed from the terms it was sold on.
    /// </summary>
    /// <returns>
    /// Null when the recomputation does not reconcile to the frozen amount, which is the only
    /// honest answer: a breakdown that does not add up to what the customer paid is worse than no
    /// breakdown, because it looks authoritative.
    /// </returns>
    /// <remarks>
    /// Safe to recompute only because every input is snapshotted. The price, the quantities, the
    /// discount terms and the calendar fraction are all on the subscription and none of them can be
    /// moved by editing the catalogue, so the calculator is being asked the same question it was
    /// asked at checkout rather than a fresh one.
    /// </remarks>
    private static FinancialDocumentAmounts? RecomposeInitialCharge(
        SubscriptionDetail subscription)
    {
        if (subscription.InitialChargeAmountMinor is not { } frozen)
        {
            return null;
        }

        var fraction = subscription.ProrationDays is { } covered &&
            subscription.ProrationTotalDays is { } total and > 0
            ? new BillingDayFraction(covered, total)
            : default;

        var charge = SubscriptionAmountCalculator.FirstPeriodCharge(
            subscription,
            fraction,
            subscription.CreatedAtUtc);

        if (charge.AmountMinor != frozen)
        {
            return null;
        }

        var (automatic, quantity) = BuiltInDiscountAttribution.Split(
            charge.BuiltInDiscountMinor,
            SubscriptionDiscountPresentation.RateOf(subscription.Price),
            QuantityDiscountCalculator.ResolveFrom(
                subscription.Plan,
                subscription.Price,
                subscription.QuantityItems).Tier?.DiscountBasisPoints,
            SubscriptionDiscountPresentation.Describe(subscription.Price));

        return new FinancialDocumentAmounts
        {
            GrossSubtotalMinor = charge.GrossAmountMinor,
            AutomaticDiscountMinor = automatic,
            QuantityDiscountMinor = quantity,
            PromotionalDiscountMinor = charge.PromotionalDiscountMinor,
            NetSubtotalMinor = charge.NetAmountMinor,
            TaxRateBasisPoints = subscription.Price.TaxRateBasisPoints,
            TaxMode = SubscriptionTaxPresentation.Describe(subscription.Price),
            TaxAmountMinor = charge.TaxAmountMinor,
            CreditAppliedMinor = 0,
            TotalMinor = charge.AmountMinor,
            AutomaticDiscountBasisPoints =
                SubscriptionDiscountPresentation.RateOf(subscription.Price),
            QuantityDiscountBasisPoints = QuantityDiscountCalculator.ResolveFrom(
                subscription.Plan,
                subscription.Price,
                subscription.QuantityItems).Tier?.DiscountBasisPoints,
            DiscountCombination = SubscriptionDiscountPresentation.Describe(subscription.Price),
            PromotionCode = charge.DiscountApplied ? subscription.Discount?.Code : null
        };
    }

    /// <summary>
    /// What can be said about a charge raised before any breakdown was recorded: the total, twice.
    /// </summary>
    private FinancialDocumentAmounts SingleGrossLine(PaymentDetail payment)
    {
        if (!_currency.TryConvert(payment.PreciseAmount, payment.CurrencyCode, out var minor))
        {
            minor = 0;
        }

        return new FinancialDocumentAmounts
        {
            GrossSubtotalMinor = minor,
            NetSubtotalMinor = minor,
            TotalMinor = minor
        };
    }

    /// <summary>
    /// A partial refund's figures, taken out of the original document rather than recalculated.
    /// </summary>
    /// <remarks>
    /// Every component is reversed in proportion to the part of the total being returned, and each
    /// group is split by largest remainder so the credit note's own subtotal, tax and total reconcile
    /// exactly against the invoice. Recalculating tax on the refunded amount instead would produce a
    /// figure a penny out of the one that was charged, which is precisely the discrepancy a credit
    /// note exists to avoid.
    /// <para>
    /// A full refund short-circuits rather than allocating, so returning everything reverses exactly
    /// what was charged with no rounding involved at all.
    /// </para>
    /// </remarks>
    internal static FinancialDocumentAmounts ReverseProportionally(
        FinancialDocumentAmounts original,
        long refundedMinor)
    {
        ArgumentNullException.ThrowIfNull(original);

        if (refundedMinor >= original.TotalMinor)
        {
            return new FinancialDocumentAmounts
            {
                GrossSubtotalMinor = original.GrossSubtotalMinor,
                AutomaticDiscountMinor = original.AutomaticDiscountMinor,
                QuantityDiscountMinor = original.QuantityDiscountMinor,
                PromotionalDiscountMinor = original.PromotionalDiscountMinor,
                NetSubtotalMinor = original.NetSubtotalMinor,
                TaxRateBasisPoints = original.TaxRateBasisPoints,
                TaxMode = original.TaxMode,
                TaxAmountMinor = original.TaxAmountMinor,
                CreditAppliedMinor = original.CreditAppliedMinor,
                TotalMinor = original.TotalMinor,
                AutomaticDiscountBasisPoints = original.AutomaticDiscountBasisPoints,
                QuantityDiscountBasisPoints = original.QuantityDiscountBasisPoints,
                DiscountCombination = original.DiscountCombination,
                PromotionCode = original.PromotionCode
            };
        }

        // The total splits into net and tax first, because those two have to add back to the
        // refunded figure exactly — that is what the subscriber's own tax return needs.
        var split = ProportionalAllocation.Split(
            refundedMinor,
            [original.NetSubtotalMinor, original.TaxAmountMinor]);

        var net = split[0];
        var tax = split[1];

        // The discounts are then scaled to that net and split between their three sources, and the
        // gross is derived as net plus what came off it. Derived rather than allocated on its own,
        // because "gross less discounts equals net" has to hold on the credit note exactly as it held
        // on the invoice — and four independently rounded figures will not oblige.
        var discountTotal =
            original.AutomaticDiscountMinor +
            original.QuantityDiscountMinor +
            original.PromotionalDiscountMinor;

        var scaledDiscount = original.NetSubtotalMinor > 0
            ? (long)((Int128)discountTotal * net / original.NetSubtotalMinor)
            : 0;

        var components = ProportionalAllocation.Split(
            scaledDiscount,
            [
                original.AutomaticDiscountMinor,
                original.QuantityDiscountMinor,
                original.PromotionalDiscountMinor
            ]);

        var gross = net + components[0] + components[1] + components[2];

        return new FinancialDocumentAmounts
        {
            GrossSubtotalMinor = gross,
            AutomaticDiscountMinor = components[0],
            QuantityDiscountMinor = components[1],
            PromotionalDiscountMinor = components[2],
            NetSubtotalMinor = net,
            TaxRateBasisPoints = original.TaxRateBasisPoints,
            TaxMode = original.TaxMode,
            TaxAmountMinor = tax,
            CreditAppliedMinor = 0,
            TotalMinor = refundedMinor,
            AutomaticDiscountBasisPoints = original.AutomaticDiscountBasisPoints,
            QuantityDiscountBasisPoints = original.QuantityDiscountBasisPoints,
            DiscountCombination = original.DiscountCombination,
            PromotionCode = original.PromotionCode
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
    private static FinancialDocumentPeriod PeriodFor(
        SubscriptionDetail subscription,
        SubscriptionChargeReference charge)
    {
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

    /// <summary>
    /// The lines a charge breaks into: one per purchased quantity item, or one for the whole plan.
    /// </summary>
    /// <remarks>
    /// Descriptive, not authoritative. The totals in <see cref="FinancialDocumentAmounts"/> are what
    /// reconcile against the bank; these say what the subscriber bought. A settlement gets a single
    /// line and defers to its two-sided breakdown, because "3 seats" is not what a mid-period plan
    /// change charged for.
    /// </remarks>
    private static List<FinancialDocumentLine> LinesFor(
        SubscriptionDetail subscription,
        SubscriptionChargeReference charge,
        FinancialDocumentAmounts amounts)
    {
        if (charge.Kind is SubscriptionChargeKind.PlanChange or
            SubscriptionChargeKind.QuantityChange)
        {
            return
            [
                new FinancialDocumentLine
                {
                    Description = charge.Kind == SubscriptionChargeKind.PlanChange
                        ? $"Plan change to {subscription.Plan.DisplayName}"
                        : $"Quantity change on {subscription.Plan.DisplayName}",
                    AmountMinor = amounts.NetSubtotalMinor
                }
            ];
        }

        if (charge.Kind == SubscriptionChargeKind.Usage)
        {
            return
            [
                new FinancialDocumentLine
                {
                    Description = $"Metered usage on {subscription.Plan.DisplayName}",
                    AmountMinor = amounts.NetSubtotalMinor
                }
            ];
        }

        if (subscription.QuantityItems.Count == 0)
        {
            return
            [
                new FinancialDocumentLine
                {
                    Description = subscription.Plan.DisplayName,
                    Quantity = 1,
                    UnitAmountMinor = subscription.Price.UnitAmountMinor,
                    AmountMinor = amounts.GrossSubtotalMinor
                }
            ];
        }

        // Per item, and the amounts are the undiscounted product of quantity and unit price —
        // discounts appear once, as their own figures, rather than being smeared across lines where
        // they would round differently and stop adding up.
        return subscription.QuantityItems
            .Select(item => new FinancialDocumentLine
            {
                Description = $"{subscription.Plan.DisplayName} — {item.UnitLabel}",
                Quantity = item.Quantity,
                UnitAmountMinor = item.UnitAmountMinor,
                AmountMinor = item.UnitAmountMinor * item.Quantity,
                ItemKey = item.ItemKey
            })
            .ToList();
    }

    private FinancialDocumentMerchant MerchantSnapshot()
    {
        var invoicing = _options.Value.Invoicing;

        return new FinancialDocumentMerchant
        {
            LegalName = invoicing.LegalName,
            Address = new BillingAddress
            {
                Line1 = invoicing.AddressLine1,
                Line2 = invoicing.AddressLine2,
                City = invoicing.City,
                Region = invoicing.Region,
                PostalCode = invoicing.PostalCode,
                CountryCode = invoicing.CountryCode
            } is { } address && !address.IsEmpty() ? address : null,
            TaxRegistrationId = invoicing.TaxRegistrationId,
            SupportEmail = invoicing.SupportEmail,
            PaymentInstructions = invoicing.PaymentInstructions
        };
    }

    /// <summary>
    /// Who the document is addressed to, falling back to the organization id.
    /// </summary>
    /// <remarks>
    /// A missing profile does not stop a document being issued. The money has moved by this point,
    /// and a subscriber with no legal name recorded still needs a record of what they paid — so the
    /// document names them by the only identifier there is. The profile requirement is enforced
    /// before the charge, where refusing costs nothing.
    /// </remarks>
    private static FinancialDocumentParty SubscriberSnapshot(
        SubscriptionDetail subscription,
        SubscriptionBillingProfile? profile) =>
        new()
        {
            OrganizationId = subscription.OrganizationId,
            LegalName = profile?.LegalName is { Length: > 0 } legalName
                ? legalName
                : subscription.OrganizationId,
            DisplayName = profile?.DisplayName,
            Address = profile?.Address,
            TaxRegistrationId = profile?.TaxRegistrationId
        };

    private static FinancialDocumentPerson ContactSnapshot(SubscriptionBillingProfile? profile) =>
        new()
        {
            Name = profile?.BillingContactName ?? string.Empty,
            Email = profile?.BillingContactEmail
        };

    /// <summary>
    /// Who asked for the thing being billed.
    /// </summary>
    /// <remarks>
    /// A renewal names <c>System renewal</c> and no user, because none acted: the clock did. Naming
    /// whoever last touched the subscription would attribute a charge to a person who may have left
    /// the company a year ago.
    /// <para>
    /// A user id with no recorded contact is named by the id. The document has to say who initiated
    /// it, and having only an identifier is a worse answer than a name but a better one than silence.
    /// </para>
    /// </remarks>
    private static FinancialDocumentPerson InitiatorSnapshot(
        SubscriptionBillingProfile? profile,
        string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new FinancialDocumentPerson { Name = "System renewal" };
        }

        var contact = profile?.Contacts
            .FirstOrDefault(item => string.Equals(item.UserId, userId, StringComparison.Ordinal));

        return new FinancialDocumentPerson
        {
            UserId = userId,
            Name = contact?.Name is { Length: > 0 } name ? name : userId,
            Email = contact?.Email
        };
    }

    /// <summary>
    /// Formats the period's boundaries in the subscriber's own zone, at issue time.
    /// </summary>
    /// <remarks>
    /// Formatted now rather than at render time so a stored document needs no timezone database to
    /// display — and so a change to the platform's zone data cannot move the dates printed on an
    /// invoice that has already been sent.
    /// </remarks>
    private static FinancialDocumentPeriod WithLocalDates(FinancialDocumentPeriod period)
    {
        period.LocalStart = LocalDate(period.StartUtc, period.TimeZoneId);
        period.LocalEnd = LocalDate(period.EndUtc, period.TimeZoneId);

        return period;
    }

    private static string LocalDate(DateTime instantUtc, string timeZoneId)
    {
        if (instantUtc == default)
        {
            return string.Empty;
        }

        var local = BillingLocalTime.TryFindTimeZone(timeZoneId, out var timeZone)
            ? BillingLocalTime.ToLocal(instantUtc, timeZone)
            : instantUtc;

        return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
