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
    /// <summary>The point a named sweep has read up to, or null if it has never run.</summary>
    Task<FinancialDocumentSweepMark?> GetAsync(
        string tenantId,
        string cursorName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a sweep's mark forward.
    /// </summary>
    /// <remarks>
    /// Forward only, compared on the whole mark rather than on its instant. A backwards write would
    /// re-scan work already documented, which is harmless but unbounded, and under two workers it
    /// would let them push each other's marks around forever.
    /// </remarks>
    Task SetAsync(
        string tenantId,
        string cursorName,
        FinancialDocumentSweepMark mark,
        CancellationToken cancellationToken);
}

/// <summary>
/// The last record a sweep accounted for: when it happened, and which one it was.
/// </summary>
/// <remarks>
/// The identifier is not decoration. An instant alone cannot page: several records can share one, so
/// resuming from an instant either re-reads them forever or steps over the ones a full page could not
/// fit. With the identifier the mark names a position in a total order, so a pass resumes exactly
/// after the record it stopped on — no overlap to re-read, and nothing skipped.
/// </remarks>
/// <param name="ReadUpToUtc">When the last accounted-for record happened.</param>
/// <param name="AfterId">
/// Which record that was. Null on a mark written before this carried one, which resumes inclusively
/// from the instant — the old behaviour, and safe because re-reading a documented record is an
/// indexed lookup that finds what it expects.
/// </param>
public readonly record struct FinancialDocumentSweepMark(
    DateTime ReadUpToUtc,
    string? AfterId);
