using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed record PaymentRefundPreflightResult(
    long MinorUnits,
    PaymentRateLimitResult? RateLimit,
    PaymentDetail? Payment,
    PaymentProvider? Provider,
    PaymentRefundOperationResult? Failure)
{
    public bool IsSuccess => Failure == null;
}
