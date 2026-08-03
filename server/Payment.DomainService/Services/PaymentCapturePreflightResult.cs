using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed record PaymentCapturePreflightResult(
    long MinorUnits,
    PaymentRateLimitResult? RateLimit,
    PaymentDetail? Payment,
    PaymentProvider? Provider,
    PaymentCaptureOperationResult? Failure)
{
    public bool IsSuccess => Failure == null;
}
