using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed record CheckoutCallbackRequest(
    string? State,
    string? SessionId,
    string? SessionResult);
