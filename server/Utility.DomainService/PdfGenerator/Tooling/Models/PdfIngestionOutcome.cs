using Utility.DomainService.PdfGenerator.Tooling.Inspection;

namespace Utility.DomainService.PdfGenerator.Tooling.Models;

/// <summary>
/// Everything the ingestion pipeline determined about one file. Deliberately carries no identity
/// or job-tracking fields (file id, timestamps, status) - those belong to the caller, which knows
/// about jobs; this is what running the pipeline over some bytes produced.
/// </summary>
public sealed class PdfIngestionOutcome
{
    public required int PageCount { get; init; }

    // Readability
    public required bool WasReadable { get; init; }
    public required bool IsReadable { get; init; }
    public bool RepairedWithQpdf { get; init; }

    // Geometry
    public bool GeometryWasReadable { get; init; } = true;
    public bool GeometryIsReadable { get; init; } = true;
    public bool GeometryNormalized { get; init; }
    public IReadOnlyList<PdfPageGeometry> PageGeometry { get; init; } = [];
    public string? GeometrySkippedReason { get; init; }

    // Signature
    public bool HasSignature { get; init; }
    public int SignatureCount { get; init; }

    // PDF/A
    public PdfAMetadataClaim MetadataClaim { get; init; } = PdfAMetadataClaim.Missing;
    public string? ClaimedProfile { get; init; }
    public string? ValidatedProfile { get; init; }
    public bool IsPdfA { get; init; }
    public bool IsCompliant { get; init; }

    /// <summary>Capped at 25 entries by <c>VeraPdfXmlParser</c> - never the full raw veraPDF report.</summary>
    public IReadOnlyList<string> PdfAFailedChecks { get; init; } = [];

    public bool PdfARepairAttempted { get; init; }
    public bool? PdfARepairSucceeded { get; init; }
    public string? FinalProfile { get; init; }

    // Result
    public required bool ContentChanged { get; init; }
    public required byte[] FinalBytes { get; init; }
    public IReadOnlyList<PdfIngestionStageRecord> Stages { get; init; } = [];

    /// <summary>Set only when the file could not be read at all, even after qpdf repair.</summary>
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
