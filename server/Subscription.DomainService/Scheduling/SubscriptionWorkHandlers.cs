using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// The handlers, each delegating to the processor that already owns its rules.
/// </summary>
/// <remarks>
/// Deliberately thin. The processors re-read the tenant's own state, decide what is still due, and
/// derive their provider idempotency keys from persisted identity — a renewal from its period and
/// attempt, a settlement from its reservation. Reimplementing any of that here would give the same
/// money two sets of rules, and the scheduler is meant to change <em>when</em> work runs, not what
/// running it means.
/// <para>
/// That is also what makes a retried item safe: the second attempt walks the same code that
/// recognizes the first attempt's charge, rather than raising a new one.
/// </para>
/// </remarks>
public sealed class ActivationSettlementWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionActivationProcessor _activation;

    public ActivationSettlementWorkHandler(ISubscriptionActivationProcessor activation) =>
        _activation = activation;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.ActivationSettlement;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _activation.ProcessDueAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

public sealed class ActivationRecoveryWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionActivationProcessor _activation;

    public ActivationRecoveryWorkHandler(ISubscriptionActivationProcessor activation) =>
        _activation = activation;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.ActivationRecovery;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _activation.RecoverStaleAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

public sealed class SettlementReservationRecoveryWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionSettlementReservationProcessor _reservations;

    public SettlementReservationRecoveryWorkHandler(
        ISubscriptionSettlementReservationProcessor reservations) =>
        _reservations = reservations;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.SettlementReservationRecovery;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _reservations.RecoverStaleAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

public sealed class RenewalWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionRenewalProcessor _renewals;

    public RenewalWorkHandler(ISubscriptionRenewalProcessor renewals) => _renewals = renewals;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.Renewal;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _renewals.ProcessDueAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

public sealed class UsagePeriodClosureWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionUsageRatingProcessor _usageRating;

    public UsagePeriodClosureWorkHandler(ISubscriptionUsageRatingProcessor usageRating) =>
        _usageRating = usageRating;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.UsagePeriodClosure;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _usageRating.CloseDuePeriodsAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

public sealed class UsageInvoiceChargeWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionUsageRatingProcessor _usageRating;

    public UsageInvoiceChargeWorkHandler(ISubscriptionUsageRatingProcessor usageRating) =>
        _usageRating = usageRating;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.UsageInvoiceCharge;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _usageRating.ChargeDueInvoicesAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

public sealed class OutboxPublicationWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionOutboxProcessor _outbox;

    public OutboxPublicationWorkHandler(ISubscriptionOutboxProcessor outbox) => _outbox = outbox;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.OutboxPublication;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        await _outbox.PublishDueAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}
