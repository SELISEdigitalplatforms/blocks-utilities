using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed record StoredPaymentMethodOperationResult(
    bool IsSuccess,
    IReadOnlyList<StoredPaymentMethodResponse>? Methods,
    PaymentFailureKind FailureKind,
    string ErrorCode,
    string ErrorMessage);
