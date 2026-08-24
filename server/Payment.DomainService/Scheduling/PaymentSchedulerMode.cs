using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Scheduling;

/// <summary>
/// Whether this process is queue-driven, decided once and never again.
/// </summary>
/// <remarks>
/// Read from configuration at construction rather than per pass. Asked repeatedly, a reload could
/// change the answer mid-flight — and a scheduler that starts draining while something else believes
/// it is idle is how the same recovery runs twice.
/// <para>
/// The payment side has less to disagree with than the subscription side: the reconciliation sweep
/// this replaces is already disabled. That makes turning the queue on a restoration of recovery
/// rather than a handover of it — but it stays a restart-only switch, because a switch that decides
/// who moves money should not be able to change under a running process.
/// </para>
/// </remarks>
public sealed class PaymentSchedulerMode
{
    public PaymentSchedulerMode(
        IOptions<PaymentOptions> options,
        ILogger<PaymentSchedulerMode> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        QueueDriven = options.Value.SchedulerEnabled;

        logger.LogInformation(
            "Payment background work mode fixed for this process QueueDriven={QueueDriven}",
            QueueDriven);
    }

    public bool QueueDriven { get; }
}
