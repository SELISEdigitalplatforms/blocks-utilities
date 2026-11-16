namespace Subscription.DomainService.Requests;

/// <summary>
/// Records that an invoice has been paid outside the card flow, for example by bank transfer.
/// </summary>
public sealed class RecordFinancialDocumentPaymentRequest
{
    /// <summary>When the money arrived. Null records it at the moment of the request.</summary>
    public DateTime? PaidAtUtc { get; set; }

    /// <summary>The payer's or the bank's reference for the transfer.</summary>
    public string? Reference { get; set; }

    /// <summary>The console may name the organization whose invoice this is.</summary>
    public string? OrganizationId { get; set; }
}
