using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling.Interfaces;

public interface IStandardPdfConverter
{
    Task<PdfTransformationResult> ConvertAsync(string inputPath, string outputPath, int timeoutSeconds, CancellationToken cancellationToken);
}
