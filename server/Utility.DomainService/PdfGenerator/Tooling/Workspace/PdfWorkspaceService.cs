using Microsoft.Extensions.Options;
using Utility.DomainService.PdfGenerator.Tooling.Interfaces;
using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;

namespace Utility.DomainService.PdfGenerator.Tooling.Workspace;

public sealed class PdfWorkspaceService : IPdfWorkspaceService
{
    private readonly IOptions<PdfToolingOptions> _options;

    public PdfWorkspaceService(IOptions<PdfToolingOptions> options)
    {
        _options = options;
    }

    public async Task<PdfWorkspace> CreateWorkspaceAsync(
        string operationId,
        Stream inputStream,
        CancellationToken cancellationToken)
    {
        var directoryPath = Path.Combine(_options.Value.TempDirectory, operationId);
        Directory.CreateDirectory(directoryPath);

        var inputPath = Path.Combine(directoryPath, "input.pdf");
        await using (var fileStream = File.Create(inputPath))
        {
            await inputStream.CopyToAsync(fileStream, cancellationToken);
        }

        return new PdfWorkspace(operationId, directoryPath, inputPath, () =>
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        });
    }
}
