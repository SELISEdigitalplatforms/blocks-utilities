using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Creates the encryption key ring for a scope that has none.
/// </summary>
/// <remarks>
/// This is the only write access the payment service has to key material, and it is
/// deliberately create-only. Creating a ring that does not exist cannot destroy anything;
/// overwriting one that does makes every provider credential and stored card in its scope
/// permanently unreadable. Rotation and key removal therefore stay with
/// <c>scripts/payment-key-vault/Provision-PaymentKeyRing.ps1</c>, where a human is present.
/// </remarks>
public interface IPaymentKeyRingStore
{
    Task<KeyRingProvisionOutcome> TryCreateAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken);
}

/// <summary>
/// Three states rather than a bool: "already there" means carry on, "could not write" means
/// stop, and collapsing them would let a vault outage look like a successful provision.
/// </summary>
public enum KeyRingProvisionOutcome
{
    Created = 0,
    AlreadyExists = 1,
    Unavailable = 2
}
