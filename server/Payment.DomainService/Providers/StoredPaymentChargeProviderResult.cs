namespace Payment.DomainService.Providers;

public sealed record StoredPaymentChargeProviderResult(
    StoredPaymentChargeOutcome Outcome,
    string? PspReference = null,
    string? ResultCode = null,
    string? SafeErrorCode = null);
