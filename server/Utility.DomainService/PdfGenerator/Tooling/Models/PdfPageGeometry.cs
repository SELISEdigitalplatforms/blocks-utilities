namespace Utility.DomainService.PdfGenerator.Tooling.Models;

/// <summary>
/// Geometry PDFBox read for a single page. Reported back to the caller so it can verify that
/// normalization did not shift page placement.
/// </summary>
public sealed class PdfPageGeometry
{
    public int Index { get; init; }

    public int Rotate { get; init; }

    /// <summary>Effective CropBox width - what a viewer shows, and what stamps are placed against.</summary>
    public double Width { get; init; }

    public double Height { get; init; }

    public IReadOnlyList<double> MediaBox { get; init; } = [];

    public IReadOnlyList<double> CropBox { get; init; } = [];

    /// <summary>Box keys rewritten as direct arrays on this page.</summary>
    public IReadOnlyList<string> RewrittenBoxes { get; init; } = [];

    /// <summary>Box keys that were stored as, or contained, indirect references before the rewrite.</summary>
    public IReadOnlyList<string> IndirectBoxes { get; init; } = [];
}
