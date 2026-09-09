namespace Utility.DomainService.PdfGenerator.Tooling.Models;

public sealed class PdfNormalizeOptions
{
    public bool DisableObjectStreams { get; init; } = true;
    public bool Linearize { get; init; } = false;
    public int TimeoutSeconds { get; init; } = 60;
}
