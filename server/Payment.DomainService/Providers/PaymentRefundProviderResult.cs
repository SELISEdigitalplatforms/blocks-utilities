namespace Payment.DomainService.Providers;

public sealed record PaymentRefundProviderResult(
    PaymentRefundProviderOutcome Outcome,
    string? ProviderRefundReference = null,
    string? ProviderStatus = null,
    string? SafeErrorCode = null);
