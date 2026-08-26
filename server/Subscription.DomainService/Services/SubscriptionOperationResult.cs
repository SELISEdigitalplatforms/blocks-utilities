using Payment.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// What a subscription operation returns, whether or not it worked.
/// </summary>
/// <remarks>
/// Expected failures are values, not exceptions. A plan that does not exist, a version that has
/// moved on, a quota that is used up — these are ordinary outcomes of a working system, and
/// throwing for them turns control flow into stack traces and loses the error code the caller
/// needs.
/// <para>
/// <see cref="PaymentFailureKind"/> is reused rather than duplicated. It is already
/// domain-neutral, and sharing it means the subscription controllers map failures to status
/// codes with exactly the same switch the payment controllers use.
/// </para>
/// </remarks>
public sealed class SubscriptionOperationResult<TValue>
{
    private SubscriptionOperationResult()
    {
    }

    public bool IsSuccess { get; private init; }

    public TValue? Value { get; private init; }

    public PaymentFailureKind FailureKind { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; private init; }

    public string CorrelationId { get; private init; } = string.Empty;

    public static SubscriptionOperationResult<TValue> Success(
        TValue value,
        string correlationId) =>
        new()
        {
            IsSuccess = true,
            Value = value,
            CorrelationId = correlationId
        };

    public static SubscriptionOperationResult<TValue> Failure(
        PaymentFailureKind kind,
        string errorCode,
        string errorMessage,
        string correlationId,
        IReadOnlyDictionary<string, string[]>? validationErrors = null) =>
        new()
        {
            IsSuccess = false,
            FailureKind = kind,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            CorrelationId = correlationId,
            ValidationErrors = validationErrors
        };

    /// <summary>
    /// Carries a failure across a change of payload type, so an orchestrating service can
    /// return an inner failure without restating its code and message.
    /// </summary>
    public SubscriptionOperationResult<TOther> ToFailure<TOther>() =>
        SubscriptionOperationResult<TOther>.Failure(
            FailureKind,
            ErrorCode ?? "subscription_failed",
            ErrorMessage ?? "The subscription operation failed.",
            CorrelationId,
            ValidationErrors);
}
