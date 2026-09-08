namespace Utility.DomainService.PdfGenerator.Tooling.Interfaces;

public interface IExternalProcessConcurrencyLimiter
{
    Task<IDisposable> AcquireAsync(CancellationToken cancellationToken);
}
