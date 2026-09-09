namespace Utility.DomainService.PdfGenerator.Tooling.Models;

public sealed class ToolHealthResult
{
    public bool Available { get; init; }
    public string? Version { get; init; }
    public string? Error { get; init; }
}
