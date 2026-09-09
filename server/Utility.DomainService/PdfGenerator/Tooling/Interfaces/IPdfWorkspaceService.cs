using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling.Interfaces;

public interface IPdfWorkspaceService
{
    Task<PdfWorkspace> CreateWorkspaceAsync(
        string operationId,
        Stream inputStream,
        CancellationToken cancellationToken);
}
