using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling;

public interface IPdfIngestionPipeline
{
    /// <summary>
    /// Runs inspection and whatever remediation it calls for over one file's bytes. Never throws
    /// for a malformed or unrepairable input - a bad file is reported in the returned outcome
    /// (<see cref="PdfIngestionOutcome.ErrorCode"/>/<see cref="PdfIngestionOutcome.ErrorMessage"/>),
    /// not thrown, so one bad file in a batch cannot abort the files after it.
    /// </summary>
    Task<PdfIngestionOutcome> ProcessAsync(byte[] inputBytes, CancellationToken cancellationToken);
}
