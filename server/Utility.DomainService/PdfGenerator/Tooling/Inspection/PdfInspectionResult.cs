namespace Utility.DomainService.PdfGenerator.Tooling.Inspection;

public sealed class PdfInspectionResult
{
    /// <summary>Both UglyToad.PdfPig and PdfSharp opened the file without throwing.</summary>
    public required bool Readable { get; init; }

    /// <summary>
    /// A guarded read of every page's MediaBox/CropBox/Width/Height did not throw. On the PdfSharp
    /// (not PdfSharpCore) version this repo carries, indirect page-box references and inheritance
    /// from an ancestor /Pages node are both already resolved transparently by the typed
    /// accessors - there is no structural signal left to inspect by the time a page's Elements are
    /// reachable, so this guarded read is the only detection mechanism, not a backstop for one.
    /// </summary>
    public bool GeometryReadable { get; init; } = true;

    /// <summary>0 when the file could not be opened at all.</summary>
    public int PageCount { get; init; }

    public bool HasSignature { get; init; }

    public int SignatureCount { get; init; }

    public PdfAMetadataClaim MetadataClaim { get; init; } = PdfAMetadataClaim.Missing;

    /// <summary>e.g. "PDF/A-2B". Set only when <see cref="MetadataClaim"/> is <see cref="PdfAMetadataClaim.Claimed"/>.</summary>
    public string? ClaimedProfile { get; init; }

    /// <summary>Set when <see cref="Readable"/> or <see cref="GeometryReadable"/> is false.</summary>
    public string? FailureReason { get; init; }
}
