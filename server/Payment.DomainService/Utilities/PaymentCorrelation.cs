namespace Payment.DomainService.Utilities;

/// <summary>
/// The correlation identifier for whatever the current asynchronous flow is doing.
/// </summary>
/// <remarks>
/// Ambient rather than a parameter threaded through every call. The alternative was adding a
/// correlation argument to <c>IPaymentWorkDispatcher</c> and its twenty-three call sites, which
/// would have carried the value only through the paths somebody remembered to update — and the
/// paths nobody remembers are exactly the ones that are hard to trace afterwards.
/// <para>
/// Set once at each boundary where work enters this service: the HTTP request, the queue
/// consumer, and each item a background loop picks up. Everything downstream reads it without
/// knowing it exists.
/// </para>
/// </remarks>
public static class PaymentCorrelation
{
    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>The current correlation id, or "none" outside any correlated flow.</summary>
    public static string Current => Ambient.Value ?? "none";

    /// <summary>Whether a correlation id has been established for this flow.</summary>
    public static bool IsSet => !string.IsNullOrWhiteSpace(Ambient.Value);

    /// <summary>
    /// Establishes the correlation id until the returned handle is disposed, restoring whatever
    /// was set before.
    /// </summary>
    /// <remarks>
    /// Restoring rather than clearing matters inside the background loops: they process many
    /// items in one flow, and an item that cleared the ambient value on the way out would leave
    /// the loop's own log lines uncorrelated for every item after the first.
    /// </remarks>
    public static IDisposable Begin(string? correlationId)
    {
        var previous = Ambient.Value;

        Ambient.Value = string.IsNullOrWhiteSpace(correlationId)
            ? previous
            : correlationId.Trim();

        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public Restore(string? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Ambient.Value = _previous;
        }
    }
}
