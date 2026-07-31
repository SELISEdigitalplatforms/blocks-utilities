namespace Payment.DomainService.Services;

/// <summary>
/// Whether a scope's key ring can be read, and which vault secret was consulted.
/// </summary>
/// <param name="IsReadable">False when the secret is missing, malformed, or unreachable.</param>
/// <param name="SecretName">The computed vault secret name, so an operator can go and look.</param>
/// <param name="UsedSharedKeyRing">
/// True when the scope has no ring of its own and fell back to the pre-migration shared ring.
/// A scope in this state works, but is not yet isolated.
/// </param>
/// <param name="ActiveKeyId">Empty when the ring is unreadable.</param>
/// <param name="FailureReason">Empty when the ring is readable.</param>
public sealed record PaymentKeyRingHealth(
    bool IsReadable,
    string SecretName,
    bool UsedSharedKeyRing,
    string ActiveKeyId,
    string FailureReason);
