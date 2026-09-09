using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling.Interfaces;

public interface IPdfAConverter
{
    Task<PdfTransformationResult> ConvertToPdfA2BAsync(string inputPath, string outputPath, int timeoutSeconds, CancellationToken cancellationToken);
}
