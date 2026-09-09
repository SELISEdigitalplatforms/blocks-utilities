using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling.Interfaces;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken);
}
