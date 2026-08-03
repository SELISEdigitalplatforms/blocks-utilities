using Payment.DomainService.Enums;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed record StoredPaymentMethodQueryResult(
    bool IsSuccess,
    IReadOnlyList<StoredPaymentMethodResponse>? Methods,
    PaymentFailureKind FailureKind,
    string ErrorCode,
    string ErrorMessage,
    PaymentRateLimitResult? RateLimit = null);
