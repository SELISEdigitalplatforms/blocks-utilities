namespace Payment.DomainService.Services;

public sealed record PaymentFundReturnDecision(
    bool IsAllowed,
    string Operation,
    string? ErrorCode = null,
    string? ErrorMessage = null);
