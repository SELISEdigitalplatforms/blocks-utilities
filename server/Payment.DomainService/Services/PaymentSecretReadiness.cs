namespace Payment.DomainService.Services;

public sealed record PaymentSecretReadiness(
    bool IsProviderTokenEncryptionAvailable,
    string? FailureCode)
{
    public static PaymentSecretReadiness Available { get; } =
        new(true, null);

    public static PaymentSecretReadiness ProviderTokenEncryptionUnavailable() =>
        new(false, "provider_token_encryption_keyring_unavailable");
}
