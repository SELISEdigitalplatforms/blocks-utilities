namespace Utility.DomainService.PdfGenerator.Tooling.Models;

public sealed class PdfNormalizeResult
{
    public bool Success { get; init; }
    public string Engine { get; init; } = "qpdf";
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}
