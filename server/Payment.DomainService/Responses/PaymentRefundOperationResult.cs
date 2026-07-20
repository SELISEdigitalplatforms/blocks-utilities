using Payment.DomainService.Enums;
using Payment.DomainService.Services;

namespace Payment.DomainService.Responses;

public sealed record PaymentRefundOperationResult(
    bool IsSuccess,
    bool IsReplay,
    PaymentRefundResponse? Refund,
    PaymentFailureKind FailureKind,
    string ErrorCode,
    string ErrorMessage,
    string CorrelationId,
    Dictionary<string, string[]>? ValidationErrors = null,
    PaymentRateLimitResult? RateLimit = null)
{
    public static PaymentRefundOperationResult Success(
        PaymentRefundResponse refund,
        string correlationId,
        bool replay = false,
        PaymentRateLimitResult? rateLimit = null) =>
        new(
            true,
            replay,
            refund,
            PaymentFailureKind.None,
            string.Empty,
            string.Empty,
            correlationId,
            RateLimit: rateLimit);

    public static PaymentRefundOperationResult Failure(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage,
        string correlationId,
        Dictionary<string, string[]>? validationErrors = null,
        PaymentRateLimitResult? rateLimit = null) =>
        new(
            false,
            false,
            null,
            failureKind,
            errorCode,
            errorMessage,
            correlationId,
            validationErrors,
            rateLimit);
}
