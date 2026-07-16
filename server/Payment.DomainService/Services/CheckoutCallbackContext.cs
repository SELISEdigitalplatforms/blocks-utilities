using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed record CheckoutCallbackContext(
    CheckoutCallbackState State,
    PaymentProvider Provider,
    PaymentDetail Payment);
