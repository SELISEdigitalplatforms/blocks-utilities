using System.Collections.Concurrent;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Where the projection backfill has reached in each tenant's roster.
/// </summary>
/// <remarks>
/// <b>Registered as a singleton, and that is the whole point of it existing.</b> The reconciler is
/// scoped, and the reconciliation background service opens a fresh scope per tenant sweep — so a
/// cursor held as a field on the reconciler was a new empty dictionary on every pass, and the
/// backfill re-read page one forever. Any tenant with more live subscriptions than one page never had
/// its later pages published at all.
/// <para>
/// Held in memory rather than persisted, deliberately, and this is the trade-off worth naming: a
/// restart forgets the position and the next pass starts from the beginning. That costs a repeat of
/// work which is idempotent and version-ordered by construction — every write the backfill makes is
/// conditional, so repeating one changes nothing — and the pass is a cycle rather than a migration, so
/// starting over is a normal state rather than a failure.
/// </para>
/// <para>
/// With several replicas each keeps its own position, so they walk the roster independently rather
/// than dividing it. That is slower to cover a large tenant than a shared durable cursor would be, and
/// it is still complete: every page is reached by every replica in turn. A durable cursor in the root
/// database would fix both, at the cost of a second scheduling record to keep correct for a pass whose
/// entire job is to be safe to repeat and safe to run late. It has not been built, and it is the
/// upgrade to make if backfill latency on the largest tenants turns out to matter.
/// </para>
/// </remarks>
public sealed class UsageProjectionBackfillCursors
{
    private readonly ConcurrentDictionary<string, string> _cursors = new(StringComparer.Ordinal);

    /// <summary>The subscription id the next pass for this tenant should resume after.</summary>
    public string? Resume(string tenantId) =>
        _cursors.TryGetValue(tenantId, out var cursor) ? cursor : null;

    /// <summary>
    /// Records where a pass stopped, or clears the position when the roster was exhausted so the
    /// next pass starts again from the beginning.
    /// </summary>
    public void Advance(string tenantId, string? resumeAfter)
    {
        if (resumeAfter is null)
        {
            _cursors.TryRemove(tenantId, out _);

            return;
        }

        _cursors[tenantId] = resumeAfter;
    }
}
