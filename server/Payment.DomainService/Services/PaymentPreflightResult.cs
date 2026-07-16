using FluentValidation;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed record PaymentPreflightResult(
    long MinorUnits,
    PaymentRateLimitResult? RateLimit,
    PaymentOperationResult? Failure)
{
    public bool IsSuccess => Failure == null;
}
