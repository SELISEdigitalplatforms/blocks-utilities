using Payment.DomainService.Enums;
using Payment.DomainService.Services;

namespace Payment.DomainService.Responses;

public sealed record PaymentCaptureOperationResult(
    bool IsSuccess,
    bool IsReplay,
    PaymentCaptureResponse? Capture,
    PaymentFailureKind FailureKind,
    string ErrorCode,
    string ErrorMessage,
    string CorrelationId,
    Dictionary<string, string[]>? ValidationErrors = null,
    PaymentRateLimitResult? RateLimit = null)
{
    public static PaymentCaptureOperationResult Success(
        PaymentCaptureResponse capture,
        string correlationId,
        bool replay = false,
        PaymentRateLimitResult? rateLimit = null) =>
        new(
            true,
            replay,
            capture,
            PaymentFailureKind.None,
            string.Empty,
            string.Empty,
            correlationId,
            RateLimit: rateLimit);

    public static PaymentCaptureOperationResult Failure(
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
