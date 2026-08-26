using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Subscription;

/// <summary>
/// An in-memory document ledger that enforces the one guarantee the real one buys with an index.
/// </summary>
/// <remarks>
/// A mock would let every test decide for itself whether a duplicate insert succeeds, which is
/// exactly the question these tests exist to answer. This keeps the rule in one place — a source key
/// may appear once — so a test that issues the same document twice gets the same answer production
/// would, without a database.
/// <para>
/// It also counts number allocations, because "did this replay allocate a second invoice number" is a
/// property no assertion on the returned document can see.
/// </para>
/// </remarks>
public sealed class FinancialDocumentLedgerFake : ISubscriptionFinancialDocumentRepository
{
    private readonly List<SubscriptionFinancialDocument> _documents = [];

    public IReadOnlyList<SubscriptionFinancialDocument> Documents => _documents;

    /// <summary>How many inserts were refused because the source already had a document.</summary>
    public int RejectedDuplicates { get; private set; }

    public Task<FinancialDocumentInsertOutcome> InsertAsync(
        SubscriptionFinancialDocument document,
        CancellationToken cancellationToken)
    {
        var existing = _documents.FirstOrDefault(item => string.Equals(
            item.SourceKey,
            document.SourceKey,
            StringComparison.Ordinal));

        if (existing is not null)
        {
            RejectedDuplicates++;

            return Task.FromResult(new FinancialDocumentInsertOutcome(existing, Inserted: false));
        }

        _documents.Add(document);

        return Task.FromResult(new FinancialDocumentInsertOutcome(document, Inserted: true));
    }

    public Task<SubscriptionFinancialDocument?> GetAsync(
        string tenantId,
        string documentId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_documents.FirstOrDefault(item =>
            item.TenantId == tenantId && item.ItemId == documentId));

    public Task<SubscriptionFinancialDocument?> FindBySourceKeyAsync(
        string tenantId,
        string sourceKey,
        CancellationToken cancellationToken) =>
        Task.FromResult(_documents.FirstOrDefault(item =>
            item.TenantId == tenantId &&
            string.Equals(item.SourceKey, sourceKey, StringComparison.Ordinal)));

    public Task<SubscriptionFinancialDocument?> FindInvoiceForPeriodAsync(
        string tenantId,
        string subscriptionId,
        DateTime periodStartUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(_documents
            .Where(item => item.TenantId == tenantId)
            .Where(item => item.SubscriptionId == subscriptionId)
            .Where(item => item.DocumentType == FinancialDocumentType.Invoice)
            .Where(item => item.Period.StartUtc == periodStartUtc)
            .OrderByDescending(item => item.IssuedAtUtc)
            .FirstOrDefault());

    public Task<FinancialDocumentPage> ListAsync(
        string tenantId,
        string organizationId,
        string? subscriptionId,
        FinancialDocumentType? documentType,
        FinancialDocumentStatus? status,
        DateTime? issuedFromUtc,
        DateTime? issuedToUtc,
        int pageSize,
        FinancialDocumentCursor? after,
        CancellationToken cancellationToken)
    {
        var matching = _documents
            .Where(item => item.TenantId == tenantId && item.OrganizationId == organizationId)
            .Where(item => subscriptionId is null || item.SubscriptionId == subscriptionId)
            .Where(item => documentType is null || item.DocumentType == documentType)
            .Where(item => status is null || item.Status == status)
            .Where(item => issuedFromUtc is null || item.IssuedAtUtc >= issuedFromUtc)
            .Where(item => issuedToUtc is null || item.IssuedAtUtc <= issuedToUtc)
            .OrderByDescending(item => item.IssuedAtUtc)
            .ThenByDescending(item => item.ItemId, StringComparer.Ordinal)
            .Where(item => after is null ||
                item.IssuedAtUtc < after.IssuedAtUtc ||
                (item.IssuedAtUtc == after.IssuedAtUtc &&
                    string.CompareOrdinal(item.ItemId, after.DocumentId) < 0))
            .Take(pageSize + 1)
            .ToList();

        var hasMore = matching.Count > pageSize;
        if (hasMore)
        {
            matching.RemoveAt(matching.Count - 1);
        }

        return Task.FromResult(new FinancialDocumentPage(matching, hasMore));
    }

    public Task<IReadOnlyList<SubscriptionFinancialDocument>> ListUndeliveredAsync(
        string tenantId,
        int maximumAttempts,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionFinancialDocument>>(
        [
            .. _documents
                .Where(item => item.TenantId == tenantId)
                .Where(item => item.Delivery.State is FinancialDocumentDeliveryState.Pending
                    or FinancialDocumentDeliveryState.Generated)
                .Where(item => item.Delivery.AttemptCount < maximumAttempts)
                .OrderBy(item => item.CreatedAtUtc)
                .Take(limit)
        ]);

    public Task<bool> TryRecordPdfAsync(
        string tenantId,
        string documentId,
        string storageId,
        string contentHash,
        long contentLength,
        DateTime generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var document = _documents.FirstOrDefault(item => item.ItemId == documentId);

        // The real repository's filter: only a document with no PDF yet accepts one. That is what
        // makes an issued PDF permanent, so the fake has to refuse the second write too.
        if (document is null || document.Delivery.StorageId is { Length: > 0 })
        {
            return Task.FromResult(false);
        }

        document.Delivery.StorageId = storageId;
        document.Delivery.ContentHash = contentHash;
        document.Delivery.ContentLength = contentLength;
        document.Delivery.GeneratedAtUtc = generatedAtUtc;
        document.Delivery.State = FinancialDocumentDeliveryState.Generated;

        return Task.FromResult(true);
    }

    public Task<bool> TryRecordMailRequestedAsync(
        string tenantId,
        string documentId,
        string messageId,
        DateTime requestedAtUtc,
        CancellationToken cancellationToken)
    {
        var document = _documents.FirstOrDefault(item => item.ItemId == documentId);

        // The real repository's filter: the first attempt to claim the publish wins, and every later
        // one is told it is repeating. That is the whole point of the field, so the fake enforces it.
        if (document is null || document.Delivery.MailRequestedAtUtc is not null)
        {
            return Task.FromResult(false);
        }

        document.Delivery.MailMessageId = messageId;
        document.Delivery.MailRequestedAtUtc = requestedAtUtc;

        return Task.FromResult(true);
    }

    public Task<bool> TryReleaseMailClaimAsync(
        string tenantId,
        string documentId,
        CancellationToken cancellationToken)
    {
        var document = _documents.FirstOrDefault(item => item.ItemId == documentId);

        // The real repository's filter: a claim whose mail was recorded as sent is never released,
        // because releasing it would authorise a second send of an invoice that already arrived.
        if (document is null || document.Delivery.EmailedAtUtc is not null)
        {
            return Task.FromResult(false);
        }

        document.Delivery.MailRequestedAtUtc = null;

        return Task.FromResult(true);
    }

    public Task<bool> TryRecordEmailAsync(
        string tenantId,
        string documentId,
        DateTime emailedAtUtc,
        CancellationToken cancellationToken)
    {
        var document = _documents.FirstOrDefault(item => item.ItemId == documentId);

        if (document is null ||
            document.Delivery.State != FinancialDocumentDeliveryState.Generated)
        {
            return Task.FromResult(false);
        }

        document.Delivery.EmailedAtUtc = emailedAtUtc;
        document.Delivery.State = FinancialDocumentDeliveryState.Delivered;

        return Task.FromResult(true);
    }

    public Task RecordDeliveryFailureAsync(
        string tenantId,
        string documentId,
        string errorCode,
        int maximumAttempts,
        DateTime attemptedAtUtc,
        CancellationToken cancellationToken)
    {
        var document = _documents.FirstOrDefault(item => item.ItemId == documentId);
        if (document is null)
        {
            return Task.CompletedTask;
        }

        document.Delivery.AttemptCount++;
        document.Delivery.LastErrorCode = errorCode;
        document.Delivery.LastAttemptAtUtc = attemptedAtUtc;

        if (document.Delivery.AttemptCount >= maximumAttempts)
        {
            document.Delivery.State = FinancialDocumentDeliveryState.Abandoned;
        }

        return Task.CompletedTask;
    }

    public Task TrySetRefundStatusAsync(
        string tenantId,
        string documentId,
        FinancialDocumentStatus status,
        CancellationToken cancellationToken)
    {
        var document = _documents.FirstOrDefault(item => item.ItemId == documentId);

        if (document is not null && document.Status != FinancialDocumentStatus.Refunded)
        {
            document.Status = status;
        }

        return Task.CompletedTask;
    }
}
