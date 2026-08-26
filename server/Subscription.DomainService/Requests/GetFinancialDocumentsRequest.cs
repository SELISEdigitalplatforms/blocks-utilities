using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

/// <summary>
/// A query over the organization's issued invoices, trial invoices and credit notes.
/// </summary>
public sealed class GetFinancialDocumentsRequest
{
    public int PageSize { get; set; } = 25;

    public string? After { get; set; }

    /// <summary>Narrow to one subscription. Null lists every subscription the organization has had.</summary>
    public string? SubscriptionId { get; set; }

    public FinancialDocumentType? DocumentType { get; set; }

    public FinancialDocumentStatus? Status { get; set; }

    /// <summary>
    /// Inclusive bounds on the issue date. A tax year is the query this exists for, so both ends are
    /// inclusive rather than half-open.
    /// </summary>
    public DateTime? IssuedFromUtc { get; set; }

    public DateTime? IssuedToUtc { get; set; }

    /// <summary>
    /// Honoured only for the platform console, using the standard subscription organization
    /// resolution policy.
    /// </summary>
    public string? OrganizationId { get; set; }
}
