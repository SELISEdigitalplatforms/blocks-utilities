using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace Payment.DomainService.Utilities;

public static class PaymentOperationResultExtensions
{
    public static PaymentOperationResult WithRateLimit(
        this PaymentOperationResult result,
        PaymentRateLimitResult rateLimit) =>
        result.IsSuccess
            ? PaymentOperationResult.Success(
                result.Payment!,
                result.CorrelationId,
                result.IsReplay,
                rateLimit.Limit,
                rateLimit.Remaining,
                rateLimit.ResetAfterSeconds)
            : PaymentOperationResult.Failure(
                result.FailureKind,
                result.ErrorCode,
                result.ErrorMessage,
                result.CorrelationId,
                result.ValidationErrors,
                result.RetryAfterSeconds,
                rateLimit.Limit,
                rateLimit.Remaining,
                rateLimit.ResetAfterSeconds);
}
