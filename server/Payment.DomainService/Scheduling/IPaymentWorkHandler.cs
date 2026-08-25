using Payment.DomainService.Enums;

namespace Payment.DomainService.Scheduling;

/// <summary>
/// What actually carries out one kind of scheduled payment work.
/// </summary>
/// <remarks>
/// Thin by design. Each processor already re-reads the tenant's own state, decides what is still
/// due, and derives its provider idempotency from persisted identity. Reimplementing any of that
/// here would give the same money two sets of rules, and the scheduler is meant to change
/// <em>when</em> work runs rather than what running it means.
/// </remarks>
public interface IPaymentWorkHandler
{
    PaymentWorkType WorkType { get; }

    Task<PaymentWorkOutcome> ExecuteAsync(
        PaymentBackgroundWork work,
        CancellationToken cancellationToken);
}

/// <summary>How an attempt ended, and what the queue should do about it.</summary>
public sealed record PaymentWorkOutcome(
    PaymentWorkResult Result,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static PaymentWorkOutcome Completed() => new(PaymentWorkResult.Completed);

    /// <summary>Worth another attempt: a timeout, an unreachable provider, a lost race.</summary>
    public static PaymentWorkOutcome Retry(string errorCode, string errorMessage) =>
        new(PaymentWorkResult.Retry, errorCode, errorMessage);

    /// <summary>Retrying cannot help. Dead-lettered without spending attempts proving it.</summary>
    public static PaymentWorkOutcome Permanent(string errorCode, string errorMessage) =>
        new(PaymentWorkResult.Permanent, errorCode, errorMessage);
}

public enum PaymentWorkResult
{
    Completed = 0,
    Retry = 1,
    Permanent = 2
}
