namespace Utility.DomainService.PdfGenerator.Tooling.Models;

public sealed class PdfFlattenResult
{
    public bool Success { get; init; }
    public string Engine { get; init; } = "pdfbox";
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}
