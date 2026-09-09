namespace Utility.DomainService.PdfGenerator.Tooling.Models;

public sealed class PdfGeometryNormalizeResult
{
    public bool Success { get; init; }
    public string Engine { get; init; } = "pdfbox";
    public int PageCount { get; init; }
    public bool Signed { get; init; }
    public bool GeometryChanged { get; init; }
    public IReadOnlyList<PdfPageGeometry> Pages { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}
