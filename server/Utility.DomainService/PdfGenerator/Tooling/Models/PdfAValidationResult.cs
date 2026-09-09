namespace Utility.DomainService.PdfGenerator.Tooling.Models;

public sealed class PdfAValidationResult
{
    public bool Success { get; init; }
    public string Engine { get; init; } = "veraPDF";
    public bool IsPdfA { get; init; }
    public bool IsCompliant { get; init; }
    public string? ProfileName { get; init; }
    public string RawReport { get; init; } = string.Empty;
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
