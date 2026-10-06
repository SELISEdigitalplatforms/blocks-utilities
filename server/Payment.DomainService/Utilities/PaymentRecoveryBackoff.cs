namespace Payment.DomainService.Utilities;

/// <summary>
/// How long a payment whose provider outcome is unknown waits before it is tried again.
/// </summary>
/// <remarks>
/// Doubles from thirty seconds to a quarter of an hour. One shared rule, because the delay a retry
/// is scheduled for and the delay the recovery pass enforces must agree: if the pass were
/// stricter than the schedule, the scheduled retry would arrive early, be skipped, and nothing
/// would ever come back for the payment.
/// </remarks>
public static class PaymentRecoveryBackoff
{
    private const int FirstDelaySeconds = 30;
    private const int MaximumDelaySeconds = 900;

    /// <param name="attempts">Initiation attempts made so far, the first one counted.</param>
    public static TimeSpan DelayFor(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(
            MaximumDelaySeconds,
            FirstDelaySeconds * Math.Pow(2, Math.Clamp(attempts - 1, 0, 10))));
}
