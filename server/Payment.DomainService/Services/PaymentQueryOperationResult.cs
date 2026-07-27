using Payment.DomainService.Enums;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed record PaymentQueryOperationResult(
    bool IsSuccess,
    PaymentListResponse? Response,
    PaymentFailureKind FailureKind,
    string ErrorCode,
    string ErrorMessage,
    string CorrelationId,
    Dictionary<string, string[]>? ValidationErrors = null,
    PaymentRateLimitResult? RateLimit = null)
{
    public static PaymentQueryOperationResult Success(
        PaymentListResponse response,
        string correlationId,
        PaymentRateLimitResult rateLimit) =>
        new(
            true,
            response,
            PaymentFailureKind.None,
            string.Empty,
            string.Empty,
            correlationId,
            RateLimit: rateLimit);

    public static PaymentQueryOperationResult Failure(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage,
        string correlationId,
        Dictionary<string, string[]>? validationErrors = null,
        PaymentRateLimitResult? rateLimit = null) =>
        new(
            false,
            null,
            failureKind,
            errorCode,
            errorMessage,
            correlationId,
            validationErrors,
            rateLimit);
}
