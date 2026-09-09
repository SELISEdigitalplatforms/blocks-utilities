using Microsoft.Extensions.Options;
using Utility.DomainService.PdfGenerator.Tooling.Interfaces;
using Utility.DomainService.PdfGenerator.Tooling.Options;

namespace Utility.DomainService.PdfGenerator.Tooling.ProcessExecution;

/// <summary>
/// Must be registered as a singleton: a scoped or transient lifetime would give each resolution
/// its own semaphore, so nothing would actually be limited across concurrent requests.
/// </summary>
public sealed class ExternalProcessConcurrencyLimiter : IExternalProcessConcurrencyLimiter, IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public ExternalProcessConcurrencyLimiter(IOptions<PdfToolingOptions> options)
    {
        var maxParallel = Math.Max(1, options.Value.MaxParallelExternalProcesses);
        _semaphore = new SemaphoreSlim(maxParallel, maxParallel);
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _semaphore.Release();
            _disposed = true;
        }
    }
}
