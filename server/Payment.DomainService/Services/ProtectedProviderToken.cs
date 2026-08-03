namespace Payment.DomainService.Services;

public sealed record ProtectedProviderToken(
    string Ciphertext,
    string Fingerprint,
    string EncryptionKeyId);
