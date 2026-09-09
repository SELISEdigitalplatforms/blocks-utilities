namespace Utility.DomainService.PdfGenerator.Tooling.Models;

/// <summary>One entry in a file's provenance: a stage the pipeline actually ran, and what happened.</summary>
public sealed class PdfIngestionStageRecord
{
    public required PdfIngestionStageName Stage { get; init; }
    public required bool Success { get; init; }

    /// <summary>
    /// True when the stage did not run to completion by design rather than failure - PDFBox's
    /// geometry tool exiting 3 SIGNED_DOCUMENT is the case this exists for: re-saving would
    /// invalidate an existing signature, so the pipeline treats it as a skip, not a failure.
    /// </summary>
    public bool Skipped { get; init; }

    public string? SkipReason { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}
