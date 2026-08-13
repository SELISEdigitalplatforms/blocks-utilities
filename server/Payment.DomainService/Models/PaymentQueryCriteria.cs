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
    /// An organization the caller asked to narrow the results to.
    /// </summary>
    /// <remarks>
    /// Deliberately a second field rather than an override of <see cref="OrganizationId"/>.
    /// This one comes from the request and can only ever <em>narrow</em>: it is applied as a
    /// further condition alongside the visibility rule above, never in place of it. A caller
    /// scoped to one organization who asks for another's payments gets an empty page rather
    /// than someone else's data.
    /// <para>
    /// Collapse these two into one field and that property is lost — the filter becomes a way
    /// to read any organization's payments by naming it.
    /// </para>
    /// </remarks>
    public string? FilterOrganizationId { get; init; }

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
