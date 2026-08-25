namespace Subscription.DomainService.Enums;

/// <summary>
/// How far a document has got towards being a PDF in the subscriber's inbox.
/// </summary>
/// <remarks>
/// Kept off the document's financial status on purpose. A failed render is an operational problem;
/// the invoice it failed to render is still issued, still numbered, and still owed. Conflating the
/// two would make a mail outage look like unbilled revenue.
/// </remarks>
public enum FinancialDocumentDeliveryState
{
    /// <summary>Issued, nothing rendered yet.</summary>
    Pending = 0,

    /// <summary>The PDF exists in storage and its hash is recorded.</summary>
    Generated = 1,

    /// <summary>The mail command has been published. Delivery itself belongs to the mail module.</summary>
    Delivered = 2,

    /// <summary>Every attempt was spent. An operator has to look, and nothing retries on its own.</summary>
    Abandoned = 3
}
