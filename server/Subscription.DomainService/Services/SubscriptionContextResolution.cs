using Payment.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// A resolved caller, or the failure explaining why there is none.
/// </summary>
public sealed class SubscriptionContextResolution
{
    private SubscriptionContextResolution()
    {
    }

    public SubscriptionContext? Context { get; private init; }

    public PaymentFailureKind FailureKind { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public bool IsSuccess => Context is not null;

    public static SubscriptionContextResolution Resolved(SubscriptionContext context) =>
        new() { Context = context };

    public static SubscriptionContextResolution Unresolved(
        PaymentFailureKind kind,
        string errorCode,
        string errorMessage) =>
        new()
        {
            FailureKind = kind,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

    public SubscriptionOperationResult<TValue> ToFailure<TValue>(string correlationId) =>
        SubscriptionOperationResult<TValue>.Failure(
            FailureKind,
            ErrorCode ?? "subscription_context_missing",
            ErrorMessage ?? "The caller context is unavailable.",
            correlationId);
}
