namespace Subscription.DomainService.Services;

/// <summary>
/// Renders an issued document to PDF, stores it, and publishes its mail command.
/// </summary>
/// <remarks>
/// Separate from issuing on purpose. Issuing allocates a number and is the point at which revenue is
/// recorded; delivery is presentation and postage. A template that throws or a storage bucket that is
/// unreachable must be able to retry all day without ever re-entering the code that allocates
/// numbers, and without the invoice looking unissued in the meantime.
/// <para>
/// Resumable at each step. A document whose PDF was stored but whose mail command was not published
/// picks up at the mail, because the PDF is already permanent — see
/// <c>ISubscriptionFinancialDocumentRepository.TryRecordPdfAsync</c>.
/// </para>
/// </remarks>
public interface ISubscriptionFinancialDocumentDeliveryService
{
    /// <returns>
    /// True when the document is delivered or needs nothing further. False when the attempt failed
    /// and is worth retrying.
    /// </returns>
    /// <remarks>
    /// <paramref name="workItemId"/> and <paramref name="attempt"/> are trace fields only, both
    /// optional -- <see cref="DeliverPendingAsync"/> calls this once per document it sweeps up with
    /// neither, since the sweep is one work item covering many documents rather than one item per
    /// document. Nothing here branches on either; they exist so a structured log line can be found
    /// by the queue item that produced it.
    /// </remarks>
    Task<bool> DeliverAsync(
        string tenantId,
        string documentId,
        CancellationToken cancellationToken,
        string? workItemId = null,
        int? attempt = null);

    /// <summary>
    /// Delivers every document in the tenant whose PDF or email never completed.
    /// </summary>
    /// <returns>How many were delivered.</returns>
    /// <remarks>
    /// The recovery path. Issuing schedules delivery for each document as it is created, but that
    /// scheduling write lives in another database and can be lost — so something has to be able to
    /// find a document nobody queued, and this is it.
    /// </remarks>
    Task<int> DeliverPendingAsync(string tenantId, CancellationToken cancellationToken);
}
