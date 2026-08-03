namespace Payment.DomainService.Providers;

public sealed record PaymentCaptureProviderResult(
    PaymentCaptureProviderOutcome Outcome,
    string? ProviderCaptureReference = null,
    string? ProviderStatus = null,
    string? SafeErrorCode = null);
