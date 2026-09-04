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
/// Issues financial documents from what the transition recorded and what the money path settled.
/// </summary>
/// <remarks>
/// Two inputs, each authoritative about a different thing. The <em>terms</em> — which plan, which
/// price, which units, which period — come from the <see cref="SubscriptionDocumentSource"/> the
/// transition appended, because those are the only version of them that cannot have moved since. The
/// <em>figures</em> come from the payment, because that is the only version of them the bank agrees
/// with.
/// <para>
/// Reading terms off the live subscription instead is correct exactly while nothing has changed since
/// the charge, which is the assumption a delayed or recovered issue breaks: an invoice for last
/// month's renewal, written after a plan change, would name this month's plan. The subscription is
/// still the fallback for an event that predates obligations being recorded, and that fallback says so
/// in the log.
/// </para>
/// <para>
/// Nothing here moves money, releases a reservation or changes a subscription. It is a bookkeeping
/// step scheduled <em>after</em> the money and state transitions commit, and its worst failure is a
/// missing document, which the sweep fixes.
/// </para>
/// </remarks>
public sealed class SubscriptionFinancialDocumentIssuer : ISubscriptionFinancialDocumentIssuer
{
    /// <summary>
    /// The sweep marks, named so two passes cannot share one and drag it around.
    /// </summary>
    /// <remarks>
    /// Public because they are the identities of stored state: an operator asked to explain why a
    /// document is missing, or to replay a stretch of history, needs to be able to name the mark they
    /// are looking at.
    /// </remarks>
    public const string SettledChargeCursor = "document-settled-charges";

    public const string RefundCursor = "document-refunds";

    public const string TrialCursor = "document-trials";

    private static readonly string[] SettledStatuses =
    [
        PaymentStatuses.Captured,
        PaymentStatuses.PartiallyRefunded,
        PaymentStatuses.Refunded
    ];

    private readonly ISubscriptionFinancialDocumentRepository _documents;
    private readonly IFinancialDocumentNumberAllocator _numbers;
    private readonly ISubscriptionBillingProfileRepository _profiles;
    private readonly ISubscriptionMerchantProfileService _merchants;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPaymentRepository _payments;
    private readonly ISubscriptionInvoiceHistoryRepository _settledCharges;
    private readonly ISubscriptionDocumentCursorRepository _cursors;
    private readonly ICurrencyMinorUnitResolver _currency;
    private readonly IOptions<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionFinancialDocumentIssuer> _logger;
    private readonly ISubscriptionWorkScheduler? _scheduler;
    private readonly TimeProvider _time;

    public SubscriptionFinancialDocumentIssuer(
        ISubscriptionFinancialDocumentRepository documents,
        IFinancialDocumentNumberAllocator numbers,
        ISubscriptionBillingProfileRepository profiles,
        ISubscriptionMerchantProfileService merchants,
        ISubscriptionRepository subscriptions,
        IPaymentRepository payments,
        ISubscriptionInvoiceHistoryRepository settledCharges,
        ISubscriptionDocumentCursorRepository cursors,
        ICurrencyMinorUnitResolver currency,
        IOptions<SubscriptionOptions> options,
        ILogger<SubscriptionFinancialDocumentIssuer> logger,
        ISubscriptionWorkScheduler? scheduler = null,
        TimeProvider? time = null)
    {
        _documents = documents;
        _numbers = numbers;
        _profiles = profiles;
        _merchants = merchants;
        _subscriptions = subscriptions;
        _payments = payments;
        _settledCharges = settledCharges;
        _cursors = cursors;
        _currency = currency;
        _options = options;
        _logger = logger;
        _scheduler = scheduler;
        _time = time ?? TimeProvider.System;
    }

    public async Task<FinancialDocumentIssueResult> IssueForPaymentAsync(
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
            //
            // Reported rather than returned as a bare null: a caller that named this payment
            // explicitly is looking at a charge it believes settled, and "not yet" is worth retrying
            // where the other no-ops here are not.
            _logger.LogInformation(
                "No document was issued because the payment is not settled " +
                "PaymentHash={PaymentHash} Status={Status}",
                PaymentLogValue.Hash(paymentDetailId),
                payment?.PaymentStatus ?? "absent");

            return FinancialDocumentIssueResult.Nothing(
                FinancialDocumentIssueOutcome.PaymentNotSettled);
        }

        var charge = SubscriptionOrderId.Parse(payment.OrderId);
        if (charge is not { SubscriptionId: { Length: > 0 } subscriptionId } ||
            charge.Kind == SubscriptionChargeKind.Unknown)
        {
            // Some other product's payment in the same tenant, when this is reached by a sweep. When
            // a queue item named the payment, it means our own announcement and this order id
            // disagree, which is worth somebody's attention rather than a silent completion — so the
            // reason travels back and the handler decides.
            _logger.LogInformation(
                "No document was issued because the payment's order id names no subscription " +
                "charge PaymentHash={PaymentHash}",
                PaymentLogValue.Hash(paymentDetailId));

            return FinancialDocumentIssueResult.Nothing(
                FinancialDocumentIssueOutcome.UnknownCharge);
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

            return FinancialDocumentIssueResult.Nothing(
                FinancialDocumentIssueOutcome.SubscriptionMissing);
        }

        var sourceKey = FinancialDocumentSourceKey.ForPayment(paymentDetailId);
        var source = SourceFor(subscription, sourceKey);
        var terms = TermsFor(subscription, source, charge, paymentDetailId);

        var amounts = AmountsFor(payment, subscription, charge, terms);
        if (amounts.TotalMinor <= 0 && payment.SubscriptionSettlement is null)
        {
            // A zero charge that is not a settlement never reached the provider, so there is no
            // money for a document to describe. A settlement of zero is different: the two sides
            // cancelled out and the subscriber is entitled to see why.
            //
            // The obligation goes too. It is not owed, and leaving it would have the sweep
            // rediscovering it forever.
            await ConsumeAsync(subscription, source, cancellationToken);

            _logger.LogInformation(
                "No document was issued because the charge came to nothing payable " +
                "PaymentHash={PaymentHash} ChargeKind={ChargeKind}",
                PaymentLogValue.Hash(paymentDetailId),
                charge.Kind);

            return FinancialDocumentIssueResult.Nothing(
                FinancialDocumentIssueOutcome.ZeroAmount);
        }

        var document = await ComposeAndIssueAsync(
            subscription,
            FinancialDocumentType.Invoice,
            sourceKey,
            payment.PaymentDate == default ? _time.GetUtcNow().UtcDateTime : payment.PaymentDate,
            amounts,
            LinesFor(terms, charge.Kind, amounts),
            source?.Period ?? SubscriptionDocumentSourceFactory.PeriodFor(subscription, charge),
            terms,
            correlationId,
            paymentDetailId: paymentDetailId,
            settlement: payment.SubscriptionSettlement,
            initiatedBy: source?.InitiatedBy,
            initiatedByUserId: payment.UserId,
            settlementReservationId: null,
            cancellationToken: cancellationToken);

        if (document is null)
        {
            // Composition itself declined — a number it could not allocate, a write that lost. The
            // money has moved and the document is owed, so this is reported as unfinished rather
            // than as a decision, and the queue item is retried.
            return FinancialDocumentIssueResult.Nothing(
                FinancialDocumentIssueOutcome.PaymentNotSettled);
        }

        await ConsumeAsync(subscription, source, cancellationToken);

        // Inserted or already present: both are the document that exists for this source, and both
        // are finished business. The unique source index is what makes the second one safe.
        return new FinancialDocumentIssueResult(
            FinancialDocumentIssueOutcome.Issued,
            document);
    }

    public async Task<int> IssueForSubscriptionAsync(
        string tenantId,
        string subscriptionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptions.GetByIdAsync(
            tenantId,
            subscriptionId,
            cancellationToken);

        return subscription is null
            ? 0
            : await DrainAsync(subscription, correlationId, cancellationToken);
    }

    /// <summary>
    /// Issues every document a subscription owes, one obligation at a time.
    /// </summary>
    /// <remarks>
    /// One failure does not stop the rest. Each obligation is independent — a trial invoice and a
    /// credit note for a later downgrade have nothing to do with each other — so a source that cannot
    /// be composed counts an attempt against itself and the others still get their documents.
    /// </remarks>
    private async Task<int> DrainAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var maximumAttempts = Math.Max(1, _options.Value.DocumentDeliveryMaxAttempts);
        var issued = 0;

        foreach (var source in subscription.PendingDocumentSources
            .Where(source => source.AttemptCount < maximumAttempts)
            .ToList())
        {
            try
            {
                if (await IssueFromSourceAsync(subscription, source, correlationId, cancellationToken)
                    is not null)
                {
                    issued++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(
                    exception,
                    "A recorded financial event could not be turned into a document " +
                    "SubscriptionHash={SubscriptionHash} DocumentType={DocumentType}",
                    PaymentLogValue.Hash(subscription.ItemId),
                    source.DocumentType);

                await _subscriptions.RecordDocumentSourceFailureAsync(
                    subscription.TenantId,
                    subscription.ItemId,
                    source.SourceKey,
                    "document_compose_failed",
                    cancellationToken);
            }
        }

        return issued;
    }

    /// <summary>
    /// Issues the one document a recorded obligation describes.
    /// </summary>
    /// <remarks>
    /// A charge defers to <see cref="IssueForPaymentAsync"/>, which reads the figures off the payment
    /// — the source froze what the charge was for, never what it came to. An opening period that
    /// activated with nothing due has no payment to defer to — the card-setup record behind it
    /// carries no money at all — so its figures were frozen on the source itself instead, the same
    /// reason a banked-credit source freezes them.
    /// </remarks>
    private async Task<SubscriptionFinancialDocument?> IssueFromSourceAsync(
        SubscriptionDetail subscription,
        SubscriptionDocumentSource source,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (source.DocumentType == FinancialDocumentType.Invoice)
        {
            // The result carries its reason for the caller that named a payment; here only the
            // document matters, because this path is draining a subscription's own obligations and
            // each one is consumed or abandoned on its own terms.
            if (source.PaymentDetailId is { Length: > 0 } paymentDetailId)
            {
                return (await IssueForPaymentAsync(
                    subscription.TenantId,
                    paymentDetailId,
                    correlationId,
                    cancellationToken)).Document;
            }

            return source.Amounts is not null
                ? await IssueOpeningDiscountInvoiceAsync(
                    subscription, source, correlationId, cancellationToken)
                : await AbandonAsync(subscription, source, cancellationToken);
        }

        if (source.DocumentType == FinancialDocumentType.TrialInvoice)
        {
            return await IssueTrialInvoiceAsync(subscription, source, correlationId, cancellationToken);
        }

        // Nothing produces a banked-credit source any more — no plan change or quantity change
        // banks credit, so SubscriptionDocumentSourceFactory no longer has a method that builds
        // one. This branch is kept to drain the sources written *before* that policy changed.
        //
        // A source is appended in the same write as the transition that caused it and issued later
        // by IssuePendingAsync, so a downgrade settled minutes before this code deployed can still
        // be sitting here unissued. Deleting this branch would strand it permanently: the balance
        // it describes has already moved, and this document is the only record of why. It can be
        // removed once no tenant has an unissued CreditNote source left.
        return await IssueBankedCreditNoteAsync(subscription, source, correlationId, cancellationToken);
    }

    public async Task<int> IssuePendingAsync(
        string tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var batch = Math.Max(1, _options.Value.DocumentDeliveryBatchSize);

        var issued = await IssueRecordedObligationsAsync(tenantId, batch, correlationId, cancellationToken);

        issued += await IssueMissedChargesAsync(tenantId, batch, correlationId, cancellationToken);

        issued += await IssueMissedRefundsAsync(tenantId, batch, correlationId, cancellationToken);

        issued += await IssueMissedTrialsAsync(tenantId, batch, correlationId, cancellationToken);

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
    /// The obligations the transitions recorded and nobody has cleared.
    /// </summary>
    /// <remarks>
    /// The first pass, and the only one that can recover a banked downgrade credit: that change takes
    /// no payment, so there is nothing else left behind to re-derive it from.
    /// <para>
    /// No time window at all. The query is a test for a non-empty array, backed by a partial index
    /// that holds only the subscriptions currently owing something, so asking "which ones, ever?"
    /// costs what asking "which ones this hour?" would — and cannot miss one older than a guess.
    /// </para>
    /// </remarks>
    private async Task<int> IssueRecordedObligationsAsync(
        string tenantId,
        int batch,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var owing = await _subscriptions.ListWithPendingDocumentSourcesAsync(
            tenantId,
            Math.Max(1, _options.Value.DocumentDeliveryMaxAttempts),
            batch,
            cancellationToken);

        var issued = 0;

        foreach (var subscription in owing)
        {
            issued += await DrainAsync(subscription, correlationId, cancellationToken);
        }

        return issued;
    }

    /// <summary>
    /// Settled charges with no document, walked forward from a stored mark.
    /// </summary>
    /// <remarks>
    /// The backstop for the obligation record itself being lost — a crash between the money
    /// committing and the append. The payment is the durable thing in that window, so this re-derives
    /// from it.
    /// <para>
    /// The mark only advances past charges this pass actually accounted for, so a batch that fills up
    /// is continued rather than skipped, and a pass that fails leaves the mark where it was.
    /// </para>
    /// </remarks>
    private async Task<int> IssueMissedChargesAsync(
        string tenantId,
        int batch,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var mark = await ReadCursorAsync(tenantId, SettledChargeCursor, cancellationToken);

        var settled = await _settledCharges.ListSettledSinceAsync(
            tenantId,
            mark.ReadUpToUtc,
            mark.AfterId,
            batch,
            cancellationToken);

        var issued = 0;

        foreach (var charge in settled)
        {
            // Asked before issuing rather than counting what came back, because almost every charge
            // in the window already has its document and the interesting number is how many did not.
            // Issuing is idempotent either way; this only keeps the count — and the warning — honest
            // about what recovery actually recovered.
            var existing = await _documents.FindBySourceKeyAsync(
                tenantId,
                FinancialDocumentSourceKey.ForPayment(charge.PaymentDetailId),
                cancellationToken);

            // The document, not the result: the result object is always present now, and testing it
            // for null would count every charge the sweep merely looked at as one it issued.
            if (existing is null &&
                (await IssueForPaymentAsync(
                    tenantId,
                    charge.PaymentDetailId,
                    correlationId,
                    cancellationToken)).Document is not null)
            {
                issued++;
            }

        }

        await AdvanceCursorAsync(
            tenantId,
            SettledChargeCursor,
            settled.Count == 0
                ? null
                : new FinancialDocumentSweepMark(
                    settled[^1].SettledAtUtc,
                    settled[^1].PaymentDetailId),
            cancellationToken);

        return issued;
    }

    /// <summary>
    /// Issues the credit notes for refunds that have confirmed since the mark.
    /// </summary>
    /// <remarks>
    /// The only route a refund reaches this module by. A refund confirms inside the payment module,
    /// which must never depend on subscriptions, so nothing there can announce it — the subscription
    /// side has to come and look. Polling for it is the price of keeping that dependency
    /// one-directional, and a small one: the query is indexed and the mark keeps it short.
    /// </remarks>
    private async Task<int> IssueMissedRefundsAsync(
        string tenantId,
        int batch,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var mark = await ReadCursorAsync(tenantId, RefundCursor, cancellationToken);

        var refunded = await _settledCharges.ListRefundedSinceAsync(
            tenantId,
            mark.ReadUpToUtc,
            mark.AfterId,
            batch,
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

                if (existing is null &&
                    await IssueRefundCreditNoteAsync(
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

        await AdvanceCursorAsync(
            tenantId,
            RefundCursor,
            refunded.Count == 0
                ? null
                : new FinancialDocumentSweepMark(
                    refunded[^1].RefundedAtUtc,
                    refunded[^1].PaymentDetailId),
            cancellationToken);

        return issued;
    }

    /// <summary>
    /// Trials with no trial invoice, walked forward from a stored mark.
    /// </summary>
    /// <remarks>
    /// The backstop for the one obligation that can predate the mechanism that records it, and that
    /// leaves no payment behind either — a trial charges nothing. Without this, a trial started while
    /// the worker was down or before this module recorded obligations never gets its document and
    /// nothing says so.
    /// </remarks>
    private async Task<int> IssueMissedTrialsAsync(
        string tenantId,
        int batch,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var mark = await ReadCursorAsync(tenantId, TrialCursor, cancellationToken);

        var trials = await _subscriptions.ListTrialsStartedSinceAsync(
            tenantId,
            mark.ReadUpToUtc,
            mark.AfterId,
            batch,
            cancellationToken);

        var issued = 0;

        foreach (var subscription in trials)
        {
            if (subscription.Trial is not { } trial)
            {
                continue;
            }

            var existing = await _documents.FindBySourceKeyAsync(
                tenantId,
                FinancialDocumentSourceKey.ForTrial(subscription.ItemId, trial.StartsAtUtc),
                cancellationToken);

            if (existing is null &&
                await IssueTrialInvoiceAsync(subscription, null, correlationId, cancellationToken)
                    is not null)
            {
                issued++;
            }

        }

        await AdvanceCursorAsync(
            tenantId,
            TrialCursor,
            trials.Count == 0
                ? null
                : new FinancialDocumentSweepMark(
                    trials[^1].Trial?.StartsAtUtc ?? mark.ReadUpToUtc,
                    trials[^1].ItemId),
            cancellationToken);

        return issued;
    }

    private async Task<FinancialDocumentSweepMark> ReadCursorAsync(
        string tenantId,
        string cursorName,
        CancellationToken cancellationToken) =>
        await _cursors.GetAsync(tenantId, cursorName, cancellationToken)
            // No mark yet, which happens once per tenant. How much pre-existing history that first
            // pass picks up is a configured decision; every pass after it starts where the last one
            // stopped, so this bounds nothing ongoing.
            ?? new FinancialDocumentSweepMark(
                _time.GetUtcNow().UtcDateTime.AddDays(
                    -Math.Max(1, _options.Value.DocumentFirstPassReachDays)),
                null);

    /// <summary>
    /// Moves a sweep's mark to the last record it accounted for.
    /// </summary>
    /// <remarks>
    /// Always, whenever anything was read. A full page is <em>not</em> a reason to hold the mark back:
    /// the page is ordered and the mark names a position in that order, so resuming after the last
    /// record read reaches the next one whether or not the page was full. Holding it back on a full
    /// page instead — which this did — meant a tenant with more than one page of history re-read the
    /// same page forever and never reached anything after it. A livelock, and a silent one: every pass
    /// looked healthy and issued nothing.
    /// <para>
    /// The record's identity travels with the instant for the same reason. Records sharing an instant
    /// are ordinary, and a mark that is only an instant either re-reads them or steps over them.
    /// </para>
    /// </remarks>
    private async Task AdvanceCursorAsync(
        string tenantId,
        string cursorName,
        FinancialDocumentSweepMark? mark,
        CancellationToken cancellationToken)
    {
        // Nothing read means nothing to move past. Deliberately not "advance to now": a pass that
        // found nothing proves only that nothing is there *yet*, and a record can still arrive with an
        // earlier instant than this pass ran at.
        if (mark is not { } reached)
        {
            return;
        }

        await _cursors.SetAsync(tenantId, cursorName, reached, cancellationToken);
    }

    private async Task<SubscriptionFinancialDocument?> IssueTrialInvoiceAsync(
        SubscriptionDetail subscription,
        SubscriptionDocumentSource? source,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (subscription.Trial is not { } trial)
        {
            return source is null
                ? null
                : await AbandonAsync(subscription, source, cancellationToken);
        }

        var terms = TermsFor(
            subscription,
            source,
            new SubscriptionChargeReference(
                subscription.ItemId,
                SubscriptionChargeKind.Initial,
                null),
            subscription.ItemId);

        var granted = terms.QuantityItems
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
            Description = $"{terms.Subject.PlanName} trial",
            AmountMinor = 0
        });

        var document = await ComposeAndIssueAsync(
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
            source?.Period ?? new FinancialDocumentPeriod
            {
                StartUtc = trial.StartsAtUtc,
                EndUtc = trial.EndsAtUtc,
                TimeZoneId = subscription.FeeSchedule.TimeZoneId
            },
            terms,
            correlationId,
            paymentDetailId: null,
            settlement: null,
            initiatedBy: source?.InitiatedBy,
            initiatedByUserId: null,
            settlementReservationId: null,
            trial: source?.Trial ?? new FinancialDocumentTrial
            {
                StartsAtUtc = trial.StartsAtUtc,
                EndsAtUtc = trial.EndsAtUtc,
                RequiresPaymentMethod = trial.RequiresPaymentMethod,
                FirstBillingAtUtc = subscription.NextFeeBillingAtUtc
            },
            cancellationToken: cancellationToken);

        if (document is not null)
        {
            await ConsumeAsync(subscription, source, cancellationToken);
        }

        return document;
    }

    /// <summary>
    /// The document for an opening period that activated with nothing due — a price discounted to
    /// zero, or one that was already zero.
    /// </summary>
    /// <remarks>
    /// Reads its figures off the source, not off a payment: the card-setup record behind this event
    /// carries no money at all, so <see cref="SubscriptionFinancialDocumentAnnouncer"/> froze the
    /// breakdown on the source itself at the moment of activation, via
    /// <see cref="RecomposeInitialCharge"/>. Otherwise the same shape as any other invoice — a real
    /// gross, a real discount and a real (zero) total, not the zero-throughout statement a trial
    /// invoice makes.
    /// </remarks>
    private async Task<SubscriptionFinancialDocument?> IssueOpeningDiscountInvoiceAsync(
        SubscriptionDetail subscription,
        SubscriptionDocumentSource source,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (source.Amounts is not { } amounts)
        {
            return await AbandonAsync(subscription, source, cancellationToken);
        }

        var terms = TermsFor(
            subscription,
            source,
            new SubscriptionChargeReference(subscription.ItemId, SubscriptionChargeKind.Initial, null),
            subscription.ItemId);

        var document = await ComposeAndIssueAsync(
            subscription,
            FinancialDocumentType.Invoice,
            source.SourceKey,
            source.OccurredAtUtc,
            amounts,
            LinesFor(terms, SubscriptionChargeKind.Initial, amounts),
            source.Period,
            terms,
            correlationId,
            paymentDetailId: null,
            settlement: null,
            initiatedBy: source.InitiatedBy,
            initiatedByUserId: source.InitiatedBy?.UserId,
            settlementReservationId: null,
            cancellationToken: cancellationToken);

        if (document is not null)
        {
            await ConsumeAsync(subscription, source, cancellationToken);
        }

        return document;
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

        // The invoice's own terms where there is one, because a refund reverses what that invoice
        // charged for — which may be a plan the subscriber left months ago.
        var terms = original is not null
            ? new DocumentTerms(original.Subject, original.Lines
                .Where(line => line.ItemKey is { Length: > 0 })
                .Select(line => new SubscriptionQuantityItem
                {
                    ItemKey = line.ItemKey!,
                    UnitLabel = line.Description,
                    Quantity = line.Quantity ?? 0,
                    UnitAmountMinor = line.UnitAmountMinor ?? 0
                })
                .ToList())
            : TermsFor(subscription, null, charge, paymentDetailId);

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
                        : $"Refund of {terms.Subject.PlanName}",
                    AmountMinor = amounts.TotalMinor
                }
            ],
            original?.Period ?? SubscriptionDocumentSourceFactory.PeriodFor(subscription, charge),
            terms,
            correlationId,
            paymentDetailId: paymentDetailId,
            settlement: null,
            initiatedBy: null,
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

    /// <summary>
    /// Issues the credit note for a change whose unused time was banked as subscription credit.
    /// </summary>
    /// <remarks>
    /// Banked credit is money the subscriber has and has not spent, so it needs a document for the
    /// same reason a refund does. Credit later <em>consumed</em> by an invoice does not: that appears
    /// as a deduction on the invoice it paid for, and issuing a second document for it would count the
    /// same value twice.
    /// <para>
    /// Every figure comes off the source, composed when the change was applied. Recomputing them here
    /// would price the credit against the plan the subscriber moved <em>to</em>, whose rate and tax
    /// mode are not the ones the credited period was charged at.
    /// </para>
    /// </remarks>
    private async Task<SubscriptionFinancialDocument?> IssueBankedCreditNoteAsync(
        SubscriptionDetail subscription,
        SubscriptionDocumentSource source,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (source.CreditedMinor <= 0)
        {
            return await AbandonAsync(subscription, source, cancellationToken);
        }

        var amounts = source.Amounts ?? new FinancialDocumentAmounts
        {
            GrossSubtotalMinor = source.CreditedMinor,
            NetSubtotalMinor = source.CreditedMinor,
            TotalMinor = source.CreditedMinor
        };

        // The invoice that charged for the period being adjusted. Linked so the subscriber can see
        // which charge the credit comes off, which is the whole reason a credit note references an
        // invoice rather than standing alone.
        var original = await _documents.FindInvoiceForPeriodAsync(
            subscription.TenantId,
            subscription.ItemId,
            source.Period.StartUtc,
            cancellationToken);

        var document = await ComposeAndIssueAsync(
            subscription,
            FinancialDocumentType.CreditNote,
            source.SourceKey,
            source.OccurredAtUtc,
            amounts,
            source.Lines.Count > 0
                ? [.. source.Lines]
                : [
                    new FinancialDocumentLine
                    {
                        Description =
                            $"Unused time credited from {source.Subject.PlanName}",
                        AmountMinor = source.CreditedMinor
                    }
                ],
            source.Period,
            new DocumentTerms(source.Subject, source.QuantityItems),
            correlationId,
            paymentDetailId: null,
            settlement: source.Settlement,
            initiatedBy: source.InitiatedBy,
            initiatedByUserId: source.InitiatedBy?.UserId,
            settlementReservationId: source.SettlementReservationId,
            originalDocument: original,
            cancellationToken: cancellationToken);

        if (document is not null)
        {
            await ConsumeAsync(subscription, source, cancellationToken);
        }

        return document;
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
        DocumentTerms terms,
        string correlationId,
        string? paymentDetailId,
        SubscriptionSettlementBreakdown? settlement,
        FinancialDocumentPerson? initiatedBy,
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

        var merchant = await _merchants.ResolveAsync(subscription.TenantId, cancellationToken);

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
            Merchant = merchant,
            Subscriber = SubscriberSnapshot(subscription, profile),
            BillingContact = ContactSnapshot(profile),
            InitiatedBy = InitiatorSnapshot(
                initiatedBy,
                profile,
                initiatedByUserId,
                documentType,
                refundId),
            Subject = terms.Subject,
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

    /// <summary>Clears an obligation whose document now exists.</summary>
    private async Task ConsumeAsync(
        SubscriptionDetail subscription,
        SubscriptionDocumentSource? source,
        CancellationToken cancellationToken)
    {
        if (source is null)
        {
            return;
        }

        await _subscriptions.TryConsumeDocumentSourceAsync(
            subscription.TenantId,
            subscription.ItemId,
            source.SourceKey,
            cancellationToken);
    }

    /// <summary>
    /// Drops an obligation that describes no document, and says so loudly.
    /// </summary>
    /// <remarks>
    /// Only for a source that is internally impossible — a credit note crediting nothing, an invoice
    /// naming no payment. Left in place it would be swept forever; treated as a document it would be a
    /// financial record of an event that did not happen. Logged as an error because it means something
    /// composed a malformed obligation, which is a defect rather than a condition.
    /// </remarks>
    private async Task<SubscriptionFinancialDocument?> AbandonAsync(
        SubscriptionDetail subscription,
        SubscriptionDocumentSource source,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            "A recorded financial event describes no document and has been discarded " +
            "SubscriptionHash={SubscriptionHash} DocumentType={DocumentType} SourceKey={SourceKey}",
            PaymentLogValue.Hash(subscription.ItemId),
            source.DocumentType,
            PaymentLogValue.Label(source.SourceKey));

        await ConsumeAsync(subscription, source, cancellationToken);

        return null;
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
                SubscriptionFinancialDocumentDeliveryService.DeliveryWorkKeyFor(
                    document.ItemId,
                    document.Delivery.ResendCount),
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

    /// <summary>The obligation matching a source key, where the transition left one.</summary>
    private static SubscriptionDocumentSource? SourceFor(
        SubscriptionDetail subscription,
        string sourceKey) =>
        subscription.PendingDocumentSources.FirstOrDefault(source => string.Equals(
            source.SourceKey,
            sourceKey,
            StringComparison.Ordinal));

    /// <summary>
    /// The terms a document describes: the frozen ones where they exist, today's where they do not.
    /// </summary>
    /// <remarks>
    /// The fallback is for events that predate obligations being recorded, and it is logged, because
    /// it is the case where a document can name the wrong plan. Silence there would make the one
    /// situation worth knowing about the one situation nothing reports.
    /// </remarks>
    private DocumentTerms TermsFor(
        SubscriptionDetail subscription,
        SubscriptionDocumentSource? source,
        SubscriptionChargeReference charge,
        string subjectHash)
    {
        if (source is not null)
        {
            return new DocumentTerms(source.Subject, source.QuantityItems);
        }

        _logger.LogInformation(
            "A financial document is being composed from the subscription as it stands, because the " +
            "event that caused it recorded no terms SubscriptionHash={SubscriptionHash} " +
            "ChargeKind={ChargeKind} SourceHash={SourceHash}",
            PaymentLogValue.Hash(subscription.ItemId),
            charge.Kind,
            PaymentLogValue.Hash(subjectHash));

        return new DocumentTerms(
            SubscriptionDocumentSourceFactory.SubjectOf(subscription),
            subscription.QuantityItems);
    }

    /// <summary>
    /// What the charge was made of, from the strongest source available.
    /// </summary>
    /// <remarks>
    /// Four sources, in order of how directly they know. A settlement — a plan change or a paid
    /// quantity increase — records a two-sided breakdown instead of a flat one, because its amount is
    /// a difference between two prorated periods rather than a single priced charge; that is read
    /// first, and read ahead of the flat fields below because every settlement payment stores a flat
    /// net of zero beside its breakdown. A renewal or overage records its own flat breakdown on the
    /// payment as it charges, so that is read verbatim. The initial charge goes out through hosted
    /// checkout, which composes no invoice and records no breakdown — but every input is snapshotted
    /// on the subscription and the amount was frozen there, so the same calculator that priced it can
    /// be asked again and its answer checked against the frozen figure. Anything older than all three
    /// is reported as a single gross line, which is all that can honestly be said about it.
    /// </remarks>
    private FinancialDocumentAmounts AmountsFor(
        PaymentDetail payment,
        SubscriptionDetail subscription,
        SubscriptionChargeReference charge,
        DocumentTerms terms)
    {
        if (payment.SubscriptionSettlement is { } settlement)
        {
            return SettlementAmounts(payment, settlement);
        }

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

        // Recomputed only while the subscription still holds the terms the charge was priced on. Once
        // a plan change has moved them, asking the calculator again is asking a different question,
        // and its answer would be a breakdown of a charge nobody made.
        if (charge.Kind == SubscriptionChargeKind.Initial &&
            DescribesCurrentTerms(terms, subscription) &&
            RecomposeInitialCharge(subscription) is { } initial)
        {
            return initial;
        }

        return SingleGrossLine(payment);
    }

    /// <summary>Whether the frozen terms and the live subscription still say the same thing.</summary>
    private static bool DescribesCurrentTerms(DocumentTerms terms, SubscriptionDetail subscription) =>
        string.Equals(terms.Subject.PriceId, subscription.Price.PriceId, StringComparison.Ordinal) &&
        terms.QuantityItems.Count == subscription.QuantityItems.Count &&
        terms.QuantityItems.All(item => subscription.QuantityItems.Any(current =>
            string.Equals(current.ItemKey, item.ItemKey, StringComparison.Ordinal) &&
            current.Quantity == item.Quantity));

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
    /// <para>
    /// Internal rather than private: <see cref="SubscriptionFinancialDocumentAnnouncer"/> reuses it
    /// to freeze the same breakdown on a source for an opening period that activated with nothing
    /// due — a card-setup activation carries no payment to read a breakdown off later, so this is
    /// called at the moment of activation instead of at issue, while <c>subscription</c> is still
    /// exactly the terms the customer was quoted.
    /// </para>
    /// </remarks>
    internal static FinancialDocumentAmounts? RecomposeInitialCharge(
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
    /// A settlement's two-sided breakdown, restated as the value/tax/credit split a document renders.
    /// </summary>
    /// <remarks>
    /// A settlement's amount is target-prorated-value less outgoing-prorated-value less credit, not a
    /// price with a discount, so there is no flat gross or discount to read — see
    /// <see cref="SubscriptionSettlementBreakdown"/>. <see cref="FinancialDocumentAmounts.TotalMinor"/>
    /// is taken from what the provider actually took rather than from the breakdown's own arithmetic,
    /// so it always agrees with the bank; the breakdown only decides how that total splits between
    /// value and tax. Each side is taxed at its own rate and can straddle a tax-mode change, so the
    /// split subtracts each side's own tax rather than recomputing one rate for the difference, which
    /// would restate the charge.
    /// </remarks>
    private FinancialDocumentAmounts SettlementAmounts(
        PaymentDetail payment,
        SubscriptionSettlementBreakdown settlement)
    {
        if (!_currency.TryConvert(payment.PreciseAmount, payment.CurrencyCode, out var total))
        {
            total = Math.Max(0, settlement.NetSettlementMinor);
        }

        var credit = settlement.CreditConsumedMinor;

        var (outgoingNet, outgoingTax) = ProratedNetAndTax(settlement.Outgoing);
        var (targetNet, targetTax) = ProratedNetAndTax(settlement.Target);

        var netDelta = targetNet - outgoingNet;
        var taxDelta = targetTax - outgoingTax;

        // An opening-stub upgrade settles two periods at once - see
        // SubscriptionSettlementBreakdown.Annual. Credit and total are spent once against the
        // combined figure, so only the nested side deltas are added in here.
        if (settlement.Annual is { } annual)
        {
            var (annualOutgoingNet, annualOutgoingTax) = ProratedNetAndTax(annual.Outgoing);
            var (annualTargetNet, annualTargetTax) = ProratedNetAndTax(annual.Target);

            netDelta += annualTargetNet - annualOutgoingNet;
            taxDelta += annualTargetTax - annualOutgoingTax;
        }

        // Summed before clamping, not clamped per period and then summed. CalculateOpeningStubUpgrade
        // combines the two periods' raw deltas before it ever clamps - combinedRawDelta = stubDelta +
        // annualDelta - so that is the sum this has to match for netWeight + taxWeight to land on
        // exactly total + credit, which is what lets Split return the split unchanged rather than
        // reallocating. Clamping each period first would drop a negative component and overstate the
        // weights, quietly trading that exactness for an approximation.
        var netWeight = Math.Max(0, netDelta);
        var taxWeight = Math.Max(0, taxDelta);

        long net;
        long tax;
        if (netWeight + taxWeight > 0)
        {
            var split = ProportionalAllocation.Split(total + credit, [netWeight, taxWeight]);
            net = split[0];
            tax = split[1];
        }
        else
        {
            // Nothing to apportion by - the honest answer is the same one SingleGrossLine gives for
            // a charge with no breakdown at all: report it as untaxed net rather than inventing a split.
            net = total + credit;
            tax = 0;
        }

        return new FinancialDocumentAmounts
        {
            GrossSubtotalMinor = net,
            NetSubtotalMinor = net,
            TaxRateBasisPoints = payment.SubscriptionTaxRateBasisPoints,
            TaxMode = payment.SubscriptionTaxMode,
            TaxAmountMinor = tax,
            CreditAppliedMinor = credit,
            TotalMinor = total
        };
    }

    /// <summary>
    /// One settlement side's prorated value, split into the part that is value and the part that is
    /// tax, in the same proportion as the side's own full period.
    /// </summary>
    /// <remarks>
    /// <see cref="SubscriptionSettlementSide.ProratedValueMinor"/> is tax-inclusive — "the whole
    /// period, tax included", counted for the part this settlement covers — so the tax share is the
    /// same proportion of it as the side's own tax is of its own period total.
    /// </remarks>
    private static (long Net, long Tax) ProratedNetAndTax(SubscriptionSettlementSide side)
    {
        var split = ProportionalAllocation.Split(
            side.ProratedValueMinor,
            [side.PeriodTotalMinor - side.TaxAmountMinor, side.TaxAmountMinor]);

        return (split[0], split[1]);
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
    /// A partial reversal's figures, taken out of the original document rather than recalculated.
    /// </summary>
    /// <remarks>
    /// Every component is reversed in proportion to the part of the total being returned, and each
    /// group is split by largest remainder so the credit note's own subtotal, tax and total reconcile
    /// exactly against the invoice. Recalculating tax on the refunded amount instead would produce a
    /// figure a penny out of the one that was charged, which is precisely the discrepancy a credit
    /// note exists to avoid.
    /// <para>
    /// A full reversal short-circuits rather than allocating, so returning everything reverses exactly
    /// what was charged with no rounding involved at all.
    /// </para>
    /// <para>
    /// Shared with <see cref="FinancialDocumentCreditComposition"/>, because unused time credited on a
    /// downgrade is the same arithmetic as a partial refund and two implementations would be two
    /// roundings.
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
    /// The lines a charge breaks into: one per purchased quantity item, or one for the whole plan.
    /// </summary>
    /// <remarks>
    /// Descriptive, not authoritative. The totals in <see cref="FinancialDocumentAmounts"/> are what
    /// reconcile against the bank; these say what the subscriber bought. A settlement gets a single
    /// line and defers to its two-sided breakdown, because "3 seats" is not what a mid-period plan
    /// change charged for.
    /// </remarks>
    private static List<FinancialDocumentLine> LinesFor(
        DocumentTerms terms,
        SubscriptionChargeKind chargeKind,
        FinancialDocumentAmounts amounts)
    {
        if (chargeKind is SubscriptionChargeKind.PlanChange or
            SubscriptionChargeKind.QuantityChange)
        {
            return
            [
                new FinancialDocumentLine
                {
                    Description = chargeKind == SubscriptionChargeKind.PlanChange
                        ? $"Plan change to {terms.Subject.PlanName}"
                        : $"Quantity change on {terms.Subject.PlanName}",
                    AmountMinor = amounts.NetSubtotalMinor
                }
            ];
        }

        if (chargeKind == SubscriptionChargeKind.Usage)
        {
            return
            [
                new FinancialDocumentLine
                {
                    Description = $"Metered usage on {terms.Subject.PlanName}",
                    AmountMinor = amounts.NetSubtotalMinor
                }
            ];
        }

        // Quantity items can describe capacity without pricing it. A flat-fee plan commonly carries
        // a seat/user item for entitlement enforcement while the selected price has no
        // QuantityItemKey; SubscriptionQuantityBuilder correctly snapshots that item's unit amount
        // as zero. Treating its presence as proof of per-unit billing produced an invoice line at
        // CHF 0.00 beside a non-zero subtotal. Only items that actually carry money belong in the
        // financial line table. If none do, this is a flat-price plan regardless of its capacity
        // metadata and the plan price is the unit price.
        var pricedItems = terms.QuantityItems
            .Where(item => item.UnitAmountMinor != 0)
            .ToList();

        if (pricedItems.Count == 0)
        {
            return
            [
                new FinancialDocumentLine
                {
                    Description = terms.Subject.PlanName,
                    Quantity = 1,
                    UnitAmountMinor = terms.Subject.UnitAmountMinor,
                    AmountMinor = amounts.GrossSubtotalMinor
                }
            ];
        }

        // Per item, and the amounts are the undiscounted product of quantity and unit price —
        // discounts appear once, as their own figures, rather than being smeared across lines where
        // they would round differently and stop adding up.
        return pricedItems
            .Select(item => new FinancialDocumentLine
            {
                Description = $"{terms.Subject.PlanName} — {item.UnitLabel}",
                Quantity = item.Quantity,
                UnitAmountMinor = item.UnitAmountMinor,
                AmountMinor = item.UnitAmountMinor * item.Quantity,
                ItemKey = item.ItemKey
            })
            .ToList();
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
    /// The identity the transition captured, first: the person may have left the company, been
    /// renamed, or never have had a billing contact recorded, and none of that should change what a
    /// document already says about who acted.
    /// <para>
    /// Failing that, the contact recorded against the acting user id — which is that user's own name
    /// and address, not the organization's billing contact. Those two are different people whenever an
    /// employee changes a plan, and printing the second is a document naming somebody who did nothing.
    /// </para>
    /// <para>
    /// With no user at all, the system is named for what it actually did. <c>System renewal</c> is
    /// reserved for the clock renewing a subscription; a refund credit note says <c>System refund</c>,
    /// because a refund is not a renewal and a document should not say it was.
    /// </para>
    /// </remarks>
    private static FinancialDocumentPerson InitiatorSnapshot(
        FinancialDocumentPerson? captured,
        SubscriptionBillingProfile? profile,
        string? userId,
        FinancialDocumentType documentType,
        string? refundId)
    {
        if (captured is { Name.Length: > 0 })
        {
            return captured;
        }

        var actingUserId = captured?.UserId is { Length: > 0 } capturedId ? capturedId : userId;

        if (string.IsNullOrWhiteSpace(actingUserId))
        {
            return new FinancialDocumentPerson { Name = SystemLabelFor(documentType, refundId) };
        }

        var contact = profile?.Contacts
            .FirstOrDefault(item => string.Equals(
                item.UserId,
                actingUserId,
                StringComparison.Ordinal));

        return new FinancialDocumentPerson
        {
            UserId = actingUserId,
            Name = contact?.Name is { Length: > 0 } name ? name : actingUserId,
            Email = contact?.Email
        };
    }

    private static string SystemLabelFor(FinancialDocumentType documentType, string? refundId)
    {
        if (refundId is { Length: > 0 })
        {
            return "System refund";
        }

        return documentType == FinancialDocumentType.Invoice ? "System renewal" : "System";
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

    /// <summary>
    /// What a document says the money was for, as of the event rather than as of now.
    /// </summary>
    /// <param name="Subject">The plan and price the event was priced against.</param>
    /// <param name="QuantityItems">The units held at the time, which is what the lines describe.</param>
    private readonly record struct DocumentTerms(
        FinancialDocumentSubject Subject,
        IReadOnlyList<SubscriptionQuantityItem> QuantityItems);
}
