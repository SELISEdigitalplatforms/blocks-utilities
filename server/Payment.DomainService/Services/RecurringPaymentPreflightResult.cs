using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed record RecurringPaymentPreflightResult(
    long MinorUnits,
    PaymentRateLimitResult? RateLimit,
    PaymentProvider? Provider,
    StoredPaymentMethod? StoredPaymentMethod,
    string? ShopperReference,
    PaymentOperationResult? Failure)
{
    public bool IsSuccess => Failure == null;
}
