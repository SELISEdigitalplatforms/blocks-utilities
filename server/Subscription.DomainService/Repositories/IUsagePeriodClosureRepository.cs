using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public enum UsageClaimOutcome
{
    /// <summary>A new claim was taken out; <c>ActiveWriterCount</c> now includes it.</summary>
    Acquired,

    /// <summary>
    /// A claim for this exact idempotency key already existed. Treated the same as
    /// <see cref="Acquired"/> by the caller — proceed — since the count was already incremented
    /// the first time this request was attempted.
    /// </summary>
    AlreadyClaimed,

    /// <summary>
    /// The period is <c>Closing</c> or <c>Closed</c>, or the usage occurred at or after the
    /// period's own <c>EffectiveEndUtc</c>. Nothing was written — no claim, no ledger record, no
    /// counter change.
    /// </summary>
    Rejected
}

/// <summary>
/// Coordinates usage writes against a period's closure, so cancellation can never rate a period
/// while a usage operation it already admitted is still in flight.
/// </summary>
public interface IUsagePeriodClosureRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Takes out a claim for one usage write, atomically against the period's current state.
    /// </summary>
    /// <param name="occurredAtUtc">
    /// When the usage itself happened — checked against the period's <c>EffectiveEndUtc</c>, not
    /// against the caller's own clock, so a request that started before the boundary but is
    /// processed slightly after it is still judged by when the usage occurred.
    /// </param>
    Task<UsageClaimOutcome> TryAcquireClaimAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        string idempotencyKey,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases a claim, idempotently. Safe to call whether the write it covered succeeded,
    /// was refused, or was never actually taken (an already-rejected or already-released claim
    /// is a no-op).
    /// </summary>
    Task ReleaseClaimAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a period from <c>Open</c> to <c>Closing</c>, idempotently, and records the boundary
    /// no further claim may be granted past. Auto-creates the record already <c>Closing</c> for a
    /// period that never took out a single claim.
    /// </summary>
    Task StartClosingAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        DateTime effectiveEndUtc,
        string closeOperationId,
        CancellationToken cancellationToken);

    Task<UsagePeriodClosure?> GetAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        CancellationToken cancellationToken);

    /// <returns>False when the period was not <c>Closing</c> — nothing to finish.</returns>
    Task<bool> TryMarkClosedAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        CancellationToken cancellationToken);
}
