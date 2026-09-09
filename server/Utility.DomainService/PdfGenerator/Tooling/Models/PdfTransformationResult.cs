namespace Utility.DomainService.PdfGenerator.Tooling.Models;

public sealed class PdfTransformationResult
{
    public required bool Success { get; init; }
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
