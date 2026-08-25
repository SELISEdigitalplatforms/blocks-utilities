namespace Subscription.DomainService.Repositories;

/// <summary>
/// How far the document-recovery sweep has read, so it never has to guess how far back to look.
/// </summary>
/// <remarks>
/// The alternative — a fixed lookback of so many hours — makes recovery a function of how long the
/// worker was away. An outage longer than the window leaves settled charges and confirmed refunds
/// permanently undocumented, with nothing recording that it happened: monitoring dressed as recovery.
/// <para>
/// A stored high-water mark has neither problem. The window begins where the last successful pass
/// ended, so nothing can fall outside it however long the gap, and the cost of a pass stays
/// proportional to the backlog rather than to the window.
/// </para>
/// </remarks>
public interface ISubscriptionDocumentCursorRepository
{
    /// <summary>The instant a named sweep has read up to, or null if it has never run.</summary>
    Task<DateTime?> GetAsync(string tenantId, string cursorName, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a sweep's mark forward.
    /// </summary>
    /// <remarks>
    /// Forward only. A backwards write would re-scan work already documented, which is harmless but
    /// unbounded, and under two workers it would let them push each other's marks around forever.
    /// </remarks>
    Task SetAsync(
        string tenantId,
        string cursorName,
        DateTime readUpToUtc,
        CancellationToken cancellationToken);
}
