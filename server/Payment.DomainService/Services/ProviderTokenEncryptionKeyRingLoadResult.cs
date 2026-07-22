namespace Payment.DomainService.Services;

public sealed record ProviderTokenEncryptionKeyRingLoadResult(
    IProviderTokenEncryptionKeyRing KeyRing,
    PaymentSecretReadiness Readiness);
