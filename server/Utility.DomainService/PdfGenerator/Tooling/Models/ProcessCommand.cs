namespace Utility.DomainService.PdfGenerator.Tooling.Models;

public sealed class ProcessCommand
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public int TimeoutSeconds { get; init; } = 60;
}
