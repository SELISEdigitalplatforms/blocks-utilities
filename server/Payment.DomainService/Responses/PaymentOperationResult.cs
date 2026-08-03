using Payment.DomainService.Enums;

namespace Payment.DomainService.Responses;

public sealed class PaymentOperationResult
{
    public bool IsSuccess { get; init; }
    public bool IsReplay { get; init; }
    public PaymentResponse? Payment { get; init; }
    public PaymentFailureKind FailureKind { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public Dictionary<string, string[]>? ValidationErrors { get; init; }
    public int? RateLimit { get; init; }
    public int? RateLimitRemaining { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public int? RateLimitResetSeconds { get; init; }
    public string CorrelationId { get; init; } = string.Empty;

    public static PaymentOperationResult Success(PaymentResponse payment, string correlationId, bool replay = false,
        int? limit = null, int? remaining = null, int? resetAfterSeconds = null) => new()
        {
            IsSuccess = true,
            Payment = payment,
            CorrelationId = correlationId,
            IsReplay = replay,
            RateLimit = limit,
            RateLimitRemaining = remaining,
            RateLimitResetSeconds = resetAfterSeconds
        };

    public static PaymentOperationResult Failure(
        PaymentFailureKind kind,
        string code,
        string message,
        string correlationId,
        Dictionary<string, string[]>? errors = null,
        int? retryAfterSeconds = null,
        int? limit = null,
        int? remaining = null,
        int? resetAfterSeconds = null) => new()
        {
            FailureKind = kind,
            ErrorCode = code,
            ErrorMessage = message,
            CorrelationId = correlationId,
            ValidationErrors = errors,
            RetryAfterSeconds = retryAfterSeconds,
            RateLimit = limit,
            RateLimitRemaining = remaining,
            RateLimitResetSeconds = resetAfterSeconds
        };
}
