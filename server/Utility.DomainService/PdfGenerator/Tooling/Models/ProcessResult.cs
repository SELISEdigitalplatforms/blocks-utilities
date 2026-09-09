namespace Utility.DomainService.PdfGenerator.Tooling.Models;

public sealed class ProcessResult
{
    public required int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public bool TimedOut { get; init; }
    public bool Success => ExitCode == 0 && !TimedOut;
}
