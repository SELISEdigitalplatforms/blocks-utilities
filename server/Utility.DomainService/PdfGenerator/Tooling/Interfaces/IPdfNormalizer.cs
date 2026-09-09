using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling.Interfaces;

public interface IPdfNormalizer
{
    Task<PdfNormalizeResult> NormalizeAsync(
        string inputPath,
        string outputPath,
        PdfNormalizeOptions options,
        CancellationToken cancellationToken);

    Task<ToolHealthResult> CheckHealthAsync(CancellationToken cancellationToken);
}
