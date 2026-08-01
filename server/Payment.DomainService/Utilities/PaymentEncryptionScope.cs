using Payment.DomainService.Entities;

namespace Payment.DomainService.Utilities;

/// <summary>
/// Names the key ring that protects a record: one ring per tenant and organization.
/// </summary>
/// <remarks>
/// An organization within a tenant may be a separate business, so two organizations under one
/// tenant are a trust boundary rather than an administrative one. A single ring for the whole
/// platform means one compromise exposes every merchant account and every stored card.
/// <para>
/// A null organization is its own scope, not a wildcard: it is the tenant-level ring that
/// serves records written before organizations existed, and organizations that have no
/// configuration of their own.
/// </para>
/// </remarks>
public readonly record struct PaymentEncryptionScope
{
    public PaymentEncryptionScope(
        string tenantId,
        string? organizationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        TenantId = tenantId;
        OrganizationId = string.IsNullOrWhiteSpace(organizationId)
            ? null
            : organizationId;
    }

    public string TenantId { get; }

    public string? OrganizationId { get; }

    /// <summary>
    /// The scope that protects a provider's credential blobs.
    /// </summary>
    public static PaymentEncryptionScope From(PaymentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new PaymentEncryptionScope(
            provider.TenantId,
            provider.OrganizationId);
    }

    /// <summary>
    /// The scope that protects a saved card's provider token. Taken from the record rather than
    /// from the caller, so webhook intake and background work — which have no request context —
    /// resolve the same ring the token was written under.
    /// </summary>
    public static PaymentEncryptionScope From(StoredPaymentMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);

        return new PaymentEncryptionScope(
            method.TenantId,
            method.OrganizationId);
    }

    public override string ToString() =>
        OrganizationId == null
            ? TenantId
            : $"{TenantId}/{OrganizationId}";
}
