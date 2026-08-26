namespace Payment.DomainService.Commands;

public sealed class ProcessPaymentWorkCommand
{
    public string TenantId { get; init; } = string.Empty;

    public bool IncludeRecovery { get; init; }

    /// <summary>
    /// The correlation id of whatever asked for this work, carried across the queue.
    /// </summary>
    /// <remarks>
    /// Without it the consumer had nothing to identify itself by and minted a fresh identifier
    /// on arrival, so a request that scheduled work and the run that performed it appeared in
    /// the logs as two unrelated events. Empty on commands enqueued before this field existed,
    /// and on any dispatch that genuinely has no originating request.
    /// </remarks>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    /// When the command was handed to the queue, so the consumer can report how long it waited.
    /// </summary>
    /// <remarks>
    /// Queue latency is otherwise invisible: work that is merely late and work that is never
    /// picked up look identical from the dispatch side, and both look like nothing at all from
    /// the consumer side.
    /// </remarks>
    public DateTime? DispatchedAtUtc { get; init; }
}
