using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling.Interfaces;

public interface IPdfGeometryNormalizer
{
    Task<PdfGeometryNormalizeResult> NormalizeGeometryAsync(
        string inputPath,
        string outputPath,
        int timeoutSeconds,
        CancellationToken cancellationToken);

    Task<ToolHealthResult> CheckHealthAsync(CancellationToken cancellationToken);
}
