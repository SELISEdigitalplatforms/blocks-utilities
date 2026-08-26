namespace Payment.DomainService.Services;

/// <summary>
/// The narrowest possible view of the secret store: read one, write one.
/// </summary>
/// <remarks>
/// Exists so the create-only rule in <see cref="PaymentKeyRingStore"/> can be tested without
/// an Azure client. That rule is the one whose failure mode is silent data loss, so it has to
/// be exercised by tests rather than only by deployment.
/// </remarks>
public interface IKeyVaultSecretGateway
{
    Task<KeyVaultSecretRead> TryReadAsync(
        string secretName,
        CancellationToken cancellationToken);

    Task<bool> TrySetAsync(
        string secretName,
        string value,
        CancellationToken cancellationToken);
}

/// <summary>
/// "Not there" and "could not ask" are different answers, and only one of them makes it safe
/// to write.
/// </summary>
public enum KeyVaultReadOutcome
{
    Found = 0,
    NotFound = 1,
    Unavailable = 2
}

public readonly record struct KeyVaultSecretRead(
    KeyVaultReadOutcome Outcome,
    string? Value)
{
    public static KeyVaultSecretRead Found(string value) =>
        new(KeyVaultReadOutcome.Found, value);

    public static readonly KeyVaultSecretRead NotFound =
        new(KeyVaultReadOutcome.NotFound, null);

    public static readonly KeyVaultSecretRead Unavailable =
        new(KeyVaultReadOutcome.Unavailable, null);
}
