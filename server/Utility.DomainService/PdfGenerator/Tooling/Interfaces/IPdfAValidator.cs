using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling.Interfaces;

public interface IPdfAValidator
{
    Task<PdfAValidationResult> ValidateAsync(
        string inputPath,
        string reportPath,
        CancellationToken cancellationToken);

    Task<ToolHealthResult> CheckHealthAsync(CancellationToken cancellationToken);
}
