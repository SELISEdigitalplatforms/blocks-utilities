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

public enum ClosureReservationOutcome
{
    /// <summary>
    /// Reserved under the caller's own <c>closeOperationId</c> — either freshly, or because the
    /// same deterministic operation id already held it (a concurrent racer finalizing the same
    /// intended cancellation, or this call itself retrying).
    /// </summary>
    Reserved,

    /// <summary>
    /// Already <c>CloseReserved</c>, <c>Closing</c> or <c>Closed</c> under a genuinely different
    /// operation id — a different cancellation outcome, not a retry of this one. Nothing was
    /// written; the caller must not proceed with its own transition.
    /// </summary>
    ConflictingOperation
}

/// <summary>
/// Coordinates usage writes against a period's closure, so cancellation can never rate a period
/// while a usage operation it already admitted is still in flight, and a cancellation that never
/// actually takes effect can never leave that period unable to accept ordinary usage.
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
    /// Stakes a claim on closing this period, before the cancellation that wants to close it has
    /// actually taken effect. Does not by itself stop new usage claims from being granted — a
    /// reservation might still be released — it only stops a <em>different</em> cancellation
    /// outcome from reserving the same period out from under this one.
    /// </summary>
    /// <remarks>
    /// A storage failure here must reach the caller as an exception, not be swallowed: proceeding
    /// with the subscription transition anyway is exactly the "cancellation succeeded but the
    /// period was never actually closed" gap this whole mechanism exists to prevent.
    /// </remarks>
    Task<ClosureReservationOutcome> TryReserveClosingAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        DateTime effectiveEndUtc,
        string closeOperationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Commits a reservation once the cancellation that made it actually took effect — moves
    /// <c>CloseReserved</c> to <c>Closing</c>, and only for the matching operation id, so a stale
    /// caller can never commit a reservation that has since moved on.
    /// </summary>
    Task TryCommitClosingAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        string closeOperationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases a reservation whose cancellation lost to something else — moves
    /// <c>CloseReserved</c> back to <c>Open</c>, and only for the matching operation id, so the
    /// period returns to accepting usage exactly as if this cancellation attempt had never
    /// happened.
    /// </summary>
    Task TryReleaseReservationAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
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
