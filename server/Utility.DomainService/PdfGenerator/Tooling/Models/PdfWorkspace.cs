namespace Utility.DomainService.PdfGenerator.Tooling.Models;

public sealed class PdfWorkspace : IAsyncDisposable
{
    private readonly Func<ValueTask> _cleanupAsync;

    public PdfWorkspace(string operationId, string directoryPath, string inputPath, Func<ValueTask> cleanupAsync)
    {
        OperationId = operationId;
        DirectoryPath = directoryPath;
        InputPath = inputPath;
        OutputPath = Path.Combine(directoryPath, "output.pdf");
        ReportPath = Path.Combine(directoryPath, "verapdf-report.xml");
        _cleanupAsync = cleanupAsync;
    }

    public string OperationId { get; }
    public string DirectoryPath { get; }
    public string InputPath { get; }
    public string OutputPath { get; }
    public string ReportPath { get; }

    public ValueTask DisposeAsync() => _cleanupAsync();
}
