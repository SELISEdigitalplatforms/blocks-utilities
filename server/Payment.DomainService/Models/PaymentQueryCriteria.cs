namespace Payment.DomainService.Models;

public sealed record PaymentQueryCriteria
{
    public string TenantId { get; init; } = string.Empty;

    /// <summary>
    /// The calling organization, which narrows the results to its own payments plus the ones
    /// made before organizations existed. Null means the caller belongs to no organization and
    /// sees the whole tenant.
    /// </summary>
    /// <remarks>
    /// Taken from the caller's context, never from the request, so nobody can list another
    /// organization's payments by asking for them.
    /// </remarks>
    public string? OrganizationId { get; init; }

    /// <summary>
    /// An organization named by the request, which <strong>replaces</strong> the scope above
    /// rather than narrowing within it.
    /// </summary>
    /// <remarks>
    /// This is a deliberate product decision, not an oversight, and it is worth stating
    /// plainly: any authenticated caller in the tenant can read any organization's payments
    /// by naming it. Organization identifiers are listable from IAM, so this is not obscure.
    /// Nothing authorises the widening — no permission, no directory check.
    /// <para>
    /// It exists because payments are consumed by server-side integrations that legitimately
    /// act for several organizations, and gating it on something the service could actually
    /// verify was declined. The tenant is still taken from the caller's token, so nothing
    /// crosses a tenant boundary; the organization boundary is, for reads, a convention.
    /// </para>
    /// </remarks>
    public string? RequestedOrganizationId { get; init; }

    public int PageSize { get; init; }
    public string[] ProviderNames { get; init; } = [];
    public string[] PaymentStatuses { get; init; } = [];
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public DateTime? PaymentDateFromUtc { get; init; }
    public DateTime? PaymentDateToUtc { get; init; }
    public string? CurrencyCode { get; init; }
    public string? OrderId { get; init; }
    public string? PaymentDetailId { get; init; }
    public string? PaymentFlow { get; init; }
    public string SortBy { get; init; } = string.Empty;
    public string SortDirection { get; init; } = string.Empty;
    public PaymentQueryCursorBoundary? CursorBoundary { get; init; }
    public bool IsBackward { get; init; }
}
