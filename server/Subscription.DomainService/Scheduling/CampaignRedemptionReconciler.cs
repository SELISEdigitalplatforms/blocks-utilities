using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Finishes a campaign redemption a crash left stuck between a subscription's own transition and
/// this ledger's paired call.
/// </summary>
/// <remarks>
/// Three call sites each write one half of a pair that is not atomic across two collections:
/// <see cref="Outbox.SubscriptionActivationProcessor.ActivateAsync"/> marks a redemption Redeemed
/// only <em>after</em> its transition to Active commits; its own
/// <see cref="Outbox.SubscriptionActivationProcessor.ExpireAsync"/> and
/// <see cref="Services.SubscriptionCancellationService.EndNowAsync"/> release one only after their
/// own transition commits. A process that dies in the gap between the two leaves a subscription
/// whose fate is already decided and a redemption row that does not know it yet.
/// <para>
/// <see cref="ICampaignRedemptionRepository.TryReleaseAsync"/> already narrows half of that gap
/// itself, by writing a durable <see cref="CampaignRedemptionState.ReleasePending"/> before its
/// second step -- see that method's own remarks. What is left for this to close: a
/// <see cref="CampaignRedemptionState.Reserved"/> row whose subscription already activated (the
/// redeem call never ran at all, not even its first step), and a
/// <see cref="CampaignRedemptionState.ReleasePending"/> row whose second step never ran.
/// </para>
/// <para>
/// Reads a subscription's actual outcome rather than re-deriving one: <c>ActivatedAtUtc</c> is set
/// once, at activation, and never cleared by anything after — including a later cancellation — so
/// it is the one field that answers "did this subscription's campaign ever get spent" independent
/// of whatever status the subscription holds today.
/// </para>
/// <para>
/// Moves no money and changes no subscriber's bill, so — like
/// <see cref="Services.SubscriptionCancellationService.ReconcileStaleClosuresAsync"/> — this runs
/// directly from the repair sweep rather than through the durable work queue that exists to keep
/// two executors from ever charging the same renewal twice. There is only one thing this could
/// possibly do to a redemption row, and doing it twice is exactly as safe as doing it once.
/// </para>
/// </remarks>
public sealed class CampaignRedemptionReconciler
{
    private readonly ICampaignRedemptionRepository _redemptions;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<CampaignRedemptionReconciler> _logger;
    private readonly TimeProvider _time;

    public CampaignRedemptionReconciler(
        ICampaignRedemptionRepository redemptions,
        ISubscriptionRepository subscriptions,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<CampaignRedemptionReconciler> logger,
        TimeProvider? time = null)
    {
        _redemptions = redemptions;
        _subscriptions = subscriptions;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Finishes whatever stale reservations this tenant has. Returns how many it resolved, purely
    /// for the sweep's own log line -- nothing here needs the count back.
    /// </summary>
    public async Task<int> ReconcileAsync(string tenantId, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;
        var reservedBefore = now.AddMinutes(-Math.Max(1, options.CampaignRedemptionGraceMinutes));
        var batchSize = Math.Max(1, options.CampaignRedemptionBatchSize);

        var stale = await _redemptions.ListStaleAsync(tenantId, reservedBefore, batchSize, cancellationToken);
        var resolved = 0;

        foreach (var redemption in stale)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return resolved;
            }

            var subscription = await _subscriptions.GetByIdAsync(
                tenantId, redemption.SubscriptionId, cancellationToken);

            if (subscription is null)
            {
                // Subscriptions are never deleted in this system -- see SubscriptionDetail's own
                // doc comment. Reaching here means either a data problem this sweep has no
                // business guessing at, or a tenant mid-migration; either way, leaving the row
                // alone is the only safe move.
                _logger.LogWarning(
                    "A stale campaign redemption names a subscription that could not be found; " +
                    "skipped SubscriptionHash={SubscriptionHash}",
                    PaymentLogValue.Hash(redemption.SubscriptionId));

                continue;
            }

            if (subscription.ActivatedAtUtc is { } activatedAtUtc)
            {
                // Set once, at activation, and never cleared afterward -- including by a later
                // cancellation. Its presence is what tells this apart from a subscription that
                // was released before ever activating, independent of whatever status it holds
                // by the time this sweep runs.
                await _redemptions.TryMarkRedeemedAsync(
                    tenantId, redemption.DiscountId, redemption.SubscriptionId, activatedAtUtc,
                    cancellationToken);
                resolved++;
            }
            else if (subscription.Status is SubscriptionStatus.IncompleteExpired or SubscriptionStatus.Canceled)
            {
                // Terminal, and never activated -- the only two ways this campaign's promise came
                // to nothing. TryReleaseAsync resumes correctly whether this row is still at
                // Reserved (its own first step never ran) or already at ReleasePending (only its
                // second step never ran); it does not need to know which.
                await _redemptions.TryReleaseAsync(
                    tenantId, redemption.DiscountId, redemption.SubscriptionId, now, cancellationToken);
                resolved++;
            }

            // Still Incomplete, not yet activated or expired: not this sweep's to decide. Its own
            // recovery machinery -- SubscriptionWorkType.ActivationRecovery -- resolves that first;
            // once it does, a later pass here finds the outcome and finishes the redemption.
        }

        if (resolved > 0)
        {
            _logger.LogInformation(
                "Repair sweep reconciled stale campaign redemptions " +
                "ReconciledCount={ReconciledCount} TenantId={TenantId}",
                resolved,
                PaymentLogValue.Id(tenantId));
        }

        return resolved;
    }
}
