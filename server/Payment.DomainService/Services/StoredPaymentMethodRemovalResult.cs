using Payment.DomainService.Enums;

namespace Payment.DomainService.Services;

public sealed record StoredPaymentMethodRemovalResult(
    StoredPaymentMethodRemovalStatus Status,
    PaymentFailureKind FailureKind,
    string ErrorCode,
    string ErrorMessage,
    PaymentRateLimitResult? RateLimit = null)
{
    public bool IsRemoved =>
        Status == StoredPaymentMethodRemovalStatus.Removed;

    public bool IsPending =>
        Status == StoredPaymentMethodRemovalStatus.Pending;
}
