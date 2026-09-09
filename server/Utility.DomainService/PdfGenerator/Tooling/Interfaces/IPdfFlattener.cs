using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling.Interfaces;

public interface IPdfFlattener
{
    Task<PdfFlattenResult> FlattenAsync(
        string inputPath,
        string outputPath,
        int timeoutSeconds,
        CancellationToken cancellationToken);

    Task<ToolHealthResult> CheckHealthAsync(CancellationToken cancellationToken);
}
