using Blocks.Genesis;

namespace Payment.DomainService.Services;

public sealed class PaymentTenantContextScopeFactory :
    IPaymentTenantContextScopeFactory
{
    public IDisposable Establish(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException(
                "A tenant identifier is required.",
                nameof(tenantId));
        }

        var normalizedTenantId = tenantId.Trim();
        var previousContext = BlocksContext.GetContext();
        var context = BlocksContext.Create(
            normalizedTenantId,
            Array.Empty<string>(),
            string.Empty,
            false,
            string.Empty,
            string.Empty,
            DateTime.MinValue,
            string.Empty,
            Array.Empty<string>(),
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            normalizedTenantId,
            string.Empty);

        BlocksContext.SetContext(context);

        var activeContext = BlocksContext.GetContext();

        if (activeContext == null ||
            !string.Equals(
                activeContext.TenantId,
                normalizedTenantId,
                StringComparison.Ordinal) ||
            !string.Equals(
                activeContext.OriginalTenantId,
                normalizedTenantId,
                StringComparison.Ordinal))
        {
            Restore(previousContext);

            throw new InvalidOperationException(
                "The payment tenant context could not be established.");
        }

        return new ContextScope(previousContext);
    }

    private static void Restore(BlocksContext? context)
    {
        if (context == null)
        {
            BlocksContext.ClearContext();
            return;
        }

        BlocksContext.SetContext(context);
    }

    private sealed class ContextScope : IDisposable
    {
        private readonly BlocksContext? _previousContext;
        private bool _disposed;

        public ContextScope(BlocksContext? previousContext)
        {
            _previousContext = previousContext;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Restore(_previousContext);
            _disposed = true;
        }
    }
}
