using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Moves a scope's stored ciphertext onto its key ring's active key.
/// </summary>
/// <remarks>
/// Without this a key can never be changed: rotation writes a new active key, but every
/// existing record still names the old one, so the old key can never be retired. It is needed
/// twice over — once to move each scope off the shared pre-migration ring, and again for every
/// rotation after that.
/// </remarks>
public interface IPaymentSecretReEncryptionService
{
    Task<PaymentSecretReEncryptionSummary> ReEncryptAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken);
}

/// <param name="Skipped">
/// Records already on the active key, or moved on by something else mid-run. A second run over
/// an unchanged scope reports everything skipped and nothing re-encrypted — that is what makes
/// the job safe to repeat.
/// </param>
/// <param name="Failed">
/// Records that could not be decrypted at all. Their key is gone; re-running will not help and
/// they need an operator.
/// </param>
public sealed record PaymentSecretReEncryptionSummary(
    int ProvidersReEncrypted,
    int StoredPaymentMethodsReEncrypted,
    int Skipped,
    int Failed);
