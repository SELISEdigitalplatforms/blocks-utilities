using Microsoft.Extensions.Options;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Worker;

/// <summary>
/// The periodic sweep that finishes work no message could carry.
/// </summary>
/// <remarks>
/// Two modes, one loop. With the durable queue off it executes every tenant's due work itself, as
/// it always has. With the queue on it stops executing and starts scheduling: it walks the roster
/// looking for tenants with work and enqueues an occurrence for each, which the scheduler then
/// claims. It is the repair path in that mode — the safety net that finds work the producers at the
/// point of change missed, or lost between two databases that share no transaction.
/// <para>
/// Never both: executing here and scheduling for the same tenant would run the same work twice, and
/// twice is a second charge.
/// </para>
/// </remarks>
/// <remarks>
/// Activation normally happens within milliseconds of a webhook, driven by the payment work
/// command. This exists for the cases nothing dispatches: a compare-and-set lost to another
/// worker that then crashed, a charge raised but never recorded, a webhook that arrived while
/// this service was restarting.
/// <para>
/// Without a tick, a subscription in any of those states stays unpaid-looking forever while the
/// customer's money has already moved — the failure nobody reports because the customer assumes
/// it is their own doing.
/// </para>
/// </remarks>
public sealed class SubscriptionReconciliationBackgroundService : BackgroundService
{
    private const int MinimumPollSeconds = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly SubscriptionSchedulerModeGate _gate;
    private readonly ILogger<SubscriptionReconciliationBackgroundService> _logger;

    public SubscriptionReconciliationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<SubscriptionOptions> options,
        SubscriptionSchedulerModeGate gate,
        ILogger<SubscriptionReconciliationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _gate = gate;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Subscription reconciliation started");

        using var timer = new PeriodicTimer(PollInterval());

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                if (!_gate.IsOpen)
                {
                    // Paused by the fleet — mid-handover, or fenced because this replica cannot
                    // prove the others can still see it. Skipped without a line of its own: the
                    // coordination service already said why, and repeating it every pass would bury
                    // the reason under the symptom.
                    continue;
                }

                // Asked every pass, not captured at startup. Projects are created at any time
                // and can subscribe immediately; a roster read once is stale the moment the next
                // one appears, and a tenant this never visits is a tenant whose renewals never
                // happen. An empty answer is a quiet pass, never the end of the loop — on a
                // fresh environment it simply means nobody has signed up yet.
                using var directoryScope = _scopeFactory.CreateScope();
                var tenantIds = await directoryScope.ServiceProvider
                    .GetRequiredService<ISubscriptionTenantDirectory>()
                    .ListTenantIdsAsync(stoppingToken);

                await SweepAsync(tenantIds, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                // One bad pass must not end the loop: the next tick is the recovery for
                // whatever went wrong here.
                _logger.LogError(
                    exception,
                    "Subscription reconciliation pass failed and will be retried");
            }
        }

        _logger.LogInformation("Subscription reconciliation stopped");
    }

    private async Task SweepAsync(
        IReadOnlyList<string> tenantIds,
        CancellationToken stoppingToken)
    {
        foreach (var tenantId in tenantIds)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await SweepTenantAsync(tenantId, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One tenant's failure ends that tenant's pass, never the sweep. The roster is
                // discovered rather than curated, so it will contain tenants this service cannot
                // reach — one never provisioned a database, one mid-migration. Letting that
                // escape would abort the loop at whatever position the bad tenant happens to
                // occupy and silently stop billing every tenant ordered after it.
                _logger.LogWarning(
                    exception,
                    "Subscription reconciliation skipped a tenant after an error " +
                    "TenantHash={TenantHash}",
                    PaymentLogValue.Hash(tenantId));
            }
        }
    }

    private async Task SweepTenantAsync(string tenantId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        // Background work has no request to read a tenant from, so one is established for
        // the duration — the same discipline the payment work consumer follows.
        using var context = services
            .GetRequiredService<IPaymentTenantContextScopeFactory>()
            .Establish(tenantId);

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantHash"] = PaymentLogValue.Hash(tenantId),
            ["SubscriptionSweepId"] = Guid.NewGuid().ToString("N")
        });

        // The fleet's mode, not configuration as it stands this second, and held for the length of
        // this tenant's pass. Read and forgotten, a mode change could have this sweep executing work
        // the queue had already started on — the same subscription renewed twice.
        var mode = _gate.ActiveMode;

        using var ticket = _gate.TryBegin(mode);

        if (ticket is null)
        {
            // Paused, or mid-handover: the mode changed between the two lines above, which is
            // exactly when doing nothing is the right answer. The next pass picks the tenant up.
            return;
        }

        if (mode == SchedulerRunMode.Queue)
        {
            await ScheduleTenantWorkAsync(services, tenantId, stoppingToken);

            return;
        }

        var activation = services.GetRequiredService<ISubscriptionActivationProcessor>();
        var renewals = services.GetRequiredService<ISubscriptionRenewalProcessor>();
        var quantityClaims = services
            .GetRequiredService<ISubscriptionSettlementReservationProcessor>();
        var usageRating = services.GetRequiredService<ISubscriptionUsageRatingProcessor>();
        var outbox = services.GetRequiredService<ISubscriptionOutboxProcessor>();

        var settled = await activation.ProcessDueAsync(tenantId, stoppingToken);
        var recovered = await activation.RecoverStaleAsync(tenantId, stoppingToken);
        // Before renewals, deliberately. A renewal prices the period ahead from the quantities
        // on the subscription, so an increase still sitting unresolved would have the next period
        // billed at the old quantity and the units granted afterwards for nothing.
        var claimsResolved = await quantityClaims.RecoverStaleAsync(tenantId, stoppingToken);
        var renewed = await renewals.ProcessDueAsync(tenantId, stoppingToken);
        var periodsClosed = await usageRating.CloseDuePeriodsAsync(tenantId, stoppingToken);
        var invoicesCharged = await usageRating.ChargeDueInvoicesAsync(tenantId, stoppingToken);
        var published = await outbox.PublishDueAsync(tenantId, stoppingToken);

        if (settled + recovered + renewed + claimsResolved +
            periodsClosed + invoicesCharged + published > 0)
        {
            _logger.LogInformation(
                "Subscription reconciliation pass completed SettledCount={SettledCount} " +
                "RecoveredCount={RecoveredCount} RenewedCount={RenewedCount} " +
                "QuantityClaimsResolvedCount={QuantityClaimsResolvedCount} " +
                "UsagePeriodsClosedCount={UsagePeriodsClosedCount} " +
                "UsageInvoicesChargedCount={UsageInvoicesChargedCount} " +
                "PublishedCount={PublishedCount}",
                settled,
                recovered,
                renewed,
                claimsResolved,
                periodsClosed,
                invoicesCharged,
                published);
        }
    }

    /// <summary>
    /// Enqueues one occurrence per work type for this tenant, instead of running it here.
    /// </summary>
    /// <remarks>
    /// Bucketed by wall clock rather than keyed per pass, so a sweep that overlaps itself — or two
    /// workers sweeping the same roster — produces one item per bucket rather than one per pass. The
    /// unique occurrence index does the rest.
    /// <para>
    /// This repair pass deliberately performs the tenant queries: it runs infrequently and exists
    /// only to heal point-of-change scheduling writes that were lost. Writing seven empty queue
    /// items per tenant per bucket would put the roster scan back into the production path and make
    /// an idle fleet look busy forever.
    /// </para>
    /// </remarks>
    private async Task ScheduleTenantWorkAsync(
        IServiceProvider services,
        string tenantId,
        CancellationToken stoppingToken)
    {
        var scheduler = services.GetRequiredService<ISubscriptionWorkScheduler>();
        var options = _options.CurrentValue;
        var now = DateTime.UtcNow;
        var bucketMinutes = Math.Max(1, options.SchedulerSweepBucketMinutes);
        var bucket = new DateTime(
            now.Year, now.Month, now.Day, now.Hour,
            now.Minute / bucketMinutes * bucketMinutes,
            0,
            DateTimeKind.Utc);

        var workKey = $"sweep:{bucket:yyyyMMddTHHmmZ}";
        var dueWorkTypes = await FindDueWorkTypesAsync(
            services, tenantId, now, options, stoppingToken);

        if (dueWorkTypes.Count == 0)
        {
            return;
        }

        // Minted, not carried — and this is the one place in the chain where that is unavoidable.
        // The sweep is not acting on anybody's request; it is looking for work no request produced.
        // Logged as minted, with what caused it, so a reader who follows a correlation id here and
        // finds no upstream knows that is the answer rather than a broken link.
        var correlationId = $"sweep-{bucket:yyyyMMddTHHmmZ}-{Guid.NewGuid():N}";
        var scheduled = 0;

        foreach (var workType in dueWorkTypes)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (await scheduler.ScheduleAsync(
                    workType,
                    tenantId,
                    workKey,
                    bucket,
                    correlationId,
                    cancellationToken: stoppingToken))
            {
                scheduled++;
            }
        }

        if (scheduled > 0)
        {
            _logger.LogInformation(
                "Repair sweep scheduled subscription work ScheduledCount={ScheduledCount} " +
                "WorkKey={WorkKey} CorrelationId={CorrelationId} CorrelationOrigin={Origin} " +
                "TenantId={TenantId}",
                scheduled,
                workKey,
                correlationId,
                // Says outright that this correlation begins here. Anything downstream carries it,
                // so the trail leads back to this line and stops — which is the truth.
                "MintedByRepairSweep",
                PaymentLogValue.Id(tenantId));
        }
    }

    private static async Task<IReadOnlyCollection<SubscriptionWorkType>> FindDueWorkTypesAsync(
        IServiceProvider services,
        string tenantId,
        DateTime now,
        SubscriptionOptions options,
        CancellationToken cancellationToken)
    {
        var due = new HashSet<SubscriptionWorkType>();
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var links = services.GetRequiredService<ISubscriptionPaymentLinkRepository>();
        var invoices = services.GetRequiredService<ISubscriptionUsageInvoiceRepository>();
        var charges = services.GetRequiredService<ISubscriptionInvoiceHistoryRepository>();
        var documents = services.GetRequiredService<ISubscriptionFinancialDocumentRepository>();

        if ((await links.ListDueAsync(tenantId, now, 1, cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.ActivationSettlement);
        }

        if ((await subscriptions.ListStaleAsync(
                tenantId,
                SubscriptionStatus.Incomplete,
                now.AddMinutes(-Math.Max(1, options.InitialChargeGraceMinutes)),
                1,
                cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.ActivationRecovery);
        }

        if ((await subscriptions.ListStaleSettlementsAsync(
                tenantId,
                now.AddMinutes(-Math.Max(1, options.SettlementReservationGraceMinutes)),
                1,
                cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.SettlementReservationRecovery);
        }

        if ((await subscriptions.ListDueForRenewalAsync(tenantId, now, 1, cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.Renewal);
        }

        if ((await subscriptions.ListDueForUsageRatingAsync(tenantId, now, 1, cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.UsagePeriodClosure);
        }

        if ((await invoices.ListDueAsync(tenantId, now, 1, cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.UsageInvoiceCharge);
        }

        if ((await subscriptions.ListWithDueEventsAsync(tenantId, now, 1, cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.OutboxPublication);
        }

        // Two questions, deliberately asked cheaply rather than precisely. "Is there a settled charge
        // or a confirmed refund in the lookback window" is one indexed read; "does each of them have
        // its document" is a read per charge, and the handler does that anyway. So a quiet tenant
        // schedules nothing and a busy one schedules one item per bucket that finds nothing to do.
        var documentWindow = now.AddHours(-Math.Max(1, options.DocumentIssueLookbackHours));

        if ((await charges.ListSettledSinceAsync(tenantId, documentWindow, 1, cancellationToken))
                .Count > 0 ||
            (await charges.ListRefundedSinceAsync(tenantId, documentWindow, 1, cancellationToken))
                .Count > 0)
        {
            due.Add(SubscriptionWorkType.FinancialDocumentIssue);
        }

        // Exact, because this one is affordable: the delivery index is partial and holds only the
        // documents that have not reached anybody yet, which is almost none of them.
        if ((await documents.ListUndeliveredAsync(
                tenantId,
                Math.Max(1, options.DocumentDeliveryMaxAttempts),
                1,
                cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.FinancialDocumentDelivery);
        }

        return due;
    }

    private TimeSpan PollInterval() =>
        TimeSpan.FromSeconds(
            Math.Max(MinimumPollSeconds, _options.CurrentValue.ReconciliationPollSeconds));
}
