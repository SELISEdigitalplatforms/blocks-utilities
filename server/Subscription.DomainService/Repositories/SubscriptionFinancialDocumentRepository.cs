using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// The document ledger, in the tenant's own database.
/// </summary>
/// <remarks>
/// Every write here is either an insert or a forward-only update to <c>Delivery</c> and
/// <c>Status</c>. There is deliberately no method that changes an amount, a party or a number: the
/// aggregate's immutability is enforced by there being no way to break it, rather than by a rule
/// somebody has to remember.
/// </remarks>
public sealed class SubscriptionFinancialDocumentRepository :
    ISubscriptionFinancialDocumentRepository
{
    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public SubscriptionFinancialDocumentRepository(IDbContextProvider db) => _db = db;

    public async Task<FinancialDocumentInsertOutcome> InsertAsync(
        SubscriptionFinancialDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await EnsureIndexesAsync(document.TenantId, cancellationToken);

        try
        {
            await Collection(document.TenantId)
                .InsertOneAsync(document, cancellationToken: cancellationToken);

            return new FinancialDocumentInsertOutcome(document, Inserted: true);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Somebody else got here first. Return theirs rather than ours: the number they
            // allocated is the one that exists, and the caller has to deliver that document or
            // nothing.
            var existing = await FindBySourceKeyAsync(
                document.TenantId,
                document.SourceKey,
                cancellationToken);

            return new FinancialDocumentInsertOutcome(existing ?? document, Inserted: false);
        }
    }

    public async Task<SubscriptionFinancialDocument?> GetAsync(
        string tenantId,
        string documentId,
        CancellationToken cancellationToken) =>
        await Collection(tenantId)
            .Find(Builders<SubscriptionFinancialDocument>.Filter.And(
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.TenantId,
                    tenantId),
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.ItemId,
                    documentId)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SubscriptionFinancialDocument?> FindBySourceKeyAsync(
        string tenantId,
        string sourceKey,
        CancellationToken cancellationToken) =>
        await Collection(tenantId)
            .Find(Builders<SubscriptionFinancialDocument>.Filter.And(
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.TenantId,
                    tenantId),
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.SourceKey,
                    sourceKey)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SubscriptionFinancialDocument?> FindInvoiceForPeriodAsync(
        string tenantId,
        string subscriptionId,
        DateTime periodStartUtc,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var filter = Builders<SubscriptionFinancialDocument>.Filter.And(
            Builders<SubscriptionFinancialDocument>.Filter.Eq(
                document => document.TenantId,
                tenantId),
            Builders<SubscriptionFinancialDocument>.Filter.Eq(
                document => document.SubscriptionId,
                subscriptionId),
            // Invoices only. A trial invoice charged nothing and a credit note is itself an
            // adjustment, so neither is something a further credit can adjust.
            Builders<SubscriptionFinancialDocument>.Filter.Eq(
                document => document.DocumentType,
                FinancialDocumentType.Invoice),
            Builders<SubscriptionFinancialDocument>.Filter.Eq(
                document => document.Period.StartUtc,
                periodStartUtc));

        // Newest first. A period can carry more than one invoice once a mid-period change has been
        // charged for, and the credit is being taken off the most recent thing charged.
        return await Collection(tenantId)
            .Find(filter)
            .SortByDescending(document => document.IssuedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FinancialDocumentPage> ListAsync(
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
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var builder = Builders<SubscriptionFinancialDocument>.Filter;
        var filters = new List<FilterDefinition<SubscriptionFinancialDocument>>
        {
            builder.Eq(document => document.TenantId, tenantId),
            builder.Eq(document => document.OrganizationId, organizationId)
        };

        if (!string.IsNullOrWhiteSpace(subscriptionId))
        {
            filters.Add(builder.Eq(document => document.SubscriptionId, subscriptionId));
        }

        if (documentType is { } type)
        {
            filters.Add(builder.Eq(document => document.DocumentType, type));
        }

        if (status is { } documentStatus)
        {
            filters.Add(builder.Eq(document => document.Status, documentStatus));
        }

        if (issuedFromUtc is { } from)
        {
            filters.Add(builder.Gte(document => document.IssuedAtUtc, from));
        }

        if (issuedToUtc is { } to)
        {
            filters.Add(builder.Lte(document => document.IssuedAtUtc, to));
        }

        if (after is not null)
        {
            filters.Add(builder.Or(
                builder.Lt(document => document.IssuedAtUtc, after.IssuedAtUtc),
                builder.And(
                    builder.Eq(document => document.IssuedAtUtc, after.IssuedAtUtc),
                    builder.Lt(document => document.ItemId, after.DocumentId))));
        }

        var items = await Collection(tenantId)
            .Find(builder.And(filters))
            .Sort(Builders<SubscriptionFinancialDocument>.Sort
                .Descending(document => document.IssuedAtUtc)
                .Descending(document => document.ItemId))
            .Limit(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new FinancialDocumentPage(items, hasMore);
    }

    public async Task<IReadOnlyList<SubscriptionFinancialDocument>> ListUndeliveredAsync(
        string tenantId,
        int maximumAttempts,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var builder = Builders<SubscriptionFinancialDocument>.Filter;

        return await Collection(tenantId)
            .Find(builder.And(
                builder.Eq(document => document.TenantId, tenantId),
                builder.In(
                    document => document.Delivery.State,
                    new[]
                    {
                        FinancialDocumentDeliveryState.Pending,
                        FinancialDocumentDeliveryState.Generated
                    }),
                builder.Lt(document => document.Delivery.AttemptCount, maximumAttempts)))
            .SortBy(document => document.CreatedAtUtc)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryRecordPdfAsync(
        string tenantId,
        string documentId,
        string storageId,
        string contentHash,
        long contentLength,
        DateTime generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await Collection(tenantId).UpdateOneAsync(
            Builders<SubscriptionFinancialDocument>.Filter.And(
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.TenantId,
                    tenantId),
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.ItemId,
                    documentId),
                // The condition that makes an issued PDF permanent. A second render cannot land,
                // whether it came from a retry, a concurrent worker or a redeployed template.
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.Delivery.StorageId,
                    null)),
            Builders<SubscriptionFinancialDocument>.Update
                .Set(document => document.Delivery.StorageId, storageId)
                .Set(document => document.Delivery.ContentHash, contentHash)
                .Set(document => document.Delivery.ContentLength, contentLength)
                .Set(document => document.Delivery.GeneratedAtUtc, generatedAtUtc)
                .Set(
                    document => document.Delivery.State,
                    FinancialDocumentDeliveryState.Generated)
                .Set(document => document.Delivery.LastErrorCode, null)
                .Set(document => document.LastUpdatedDateUtc, generatedAtUtc),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryRecordMailRequestedAsync(
        string tenantId,
        string documentId,
        string messageId,
        DateTime requestedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await Collection(tenantId).UpdateOneAsync(
            Builders<SubscriptionFinancialDocument>.Filter.And(
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.TenantId,
                    tenantId),
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.ItemId,
                    documentId),
                // Nobody has claimed the publish yet. Two workers racing here means exactly one
                // publishes without knowing it might be repeating; the loser is told, and says so.
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.Delivery.MailRequestedAtUtc,
                    null)),
            Builders<SubscriptionFinancialDocument>.Update
                .Set(document => document.Delivery.MailMessageId, messageId)
                .Set(document => document.Delivery.MailRequestedAtUtc, requestedAtUtc),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<FinancialDocumentResendOutcome?> TryReopenDeliveryAsync(
        string tenantId,
        string documentId,
        CancellationToken cancellationToken)
    {
        var stored = await GetAsync(tenantId, documentId, cancellationToken);

        if (stored is null)
        {
            return null;
        }

        // Read first, and safe to: the only thing decided from the read is whether a PDF exists, and
        // the storage id is written once and never changed. Nothing else about the reopening depends
        // on state that another writer could move underneath it.
        var reopened = await Collection(tenantId).FindOneAndUpdateAsync(
            Builders<SubscriptionFinancialDocument>.Filter.And(
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.TenantId,
                    tenantId),
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.ItemId,
                    documentId),
                // Only reopen something that is actually finished with. A document whose send is still
                // outstanding needs no reopening, and this filter is what makes two concurrent resends
                // collapse: the first flips the document into the outstanding state, so the second no
                // longer matches and joins it instead of minting a second generation.
                Builders<SubscriptionFinancialDocument>.Filter.Not(SendOutstanding())),
            Builders<SubscriptionFinancialDocument>.Update
                // The claim goes back, which is what allows one more send.
                .Set(document => document.Delivery.MailRequestedAtUtc, null)
                .Set(document => document.Delivery.EmailedAtUtc, null)
                // Back to the state the PDF justifies. A document that was rendered does not need
                // rendering again — an issued PDF is never regenerated — so it resumes at the mail.
                .Set(
                    document => document.Delivery.State,
                    stored.Delivery.StorageId is { Length: > 0 }
                        ? FinancialDocumentDeliveryState.Generated
                        : FinancialDocumentDeliveryState.Pending)
                .Set(document => document.Delivery.AttemptCount, 0)
                .Set(document => document.Delivery.LastErrorCode, null)
                // Incremented in the same write, so the generation a caller is handed is the one this
                // reopening owns rather than one it read a moment ago.
                .Inc(document => document.Delivery.ResendCount, 1)
                .Set(document => document.LastUpdatedDateUtc, DateTime.UtcNow),
            new FindOneAndUpdateOptions<SubscriptionFinancialDocument>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        if (reopened is not null)
        {
            return new FinancialDocumentResendOutcome(
                reopened.Delivery.ResendCount,
                JoinedPending: false);
        }

        // Nothing matched, which means a send is already outstanding: either this document's first
        // delivery has not gone out yet, or another resend got here first. Either way one email is
        // coming and a second generation would only add a queue item that finds the claim taken and
        // sends nothing. Re-read for the generation that send belongs to.
        var current = await GetAsync(tenantId, documentId, cancellationToken);

        return current is null
            ? null
            : new FinancialDocumentResendOutcome(
                current.Delivery.ResendCount,
                JoinedPending: true);
    }

    /// <summary>
    /// Whether a send for this document is still going to happen without anybody asking again.
    /// </summary>
    /// <remarks>
    /// Unclaimed, unsent, and not given up on. A claim that is <em>taken</em> means an attempt is in
    /// flight or its outcome was never established, and neither of those is a send anybody can still
    /// expect — which is exactly when a resend is the right thing to ask for.
    /// <para>
    /// A null comparison matches a missing field as well as an explicit null, which is what makes this
    /// read correctly against documents written before the mail claim existed.
    /// </para>
    /// </remarks>
    private static FilterDefinition<SubscriptionFinancialDocument> SendOutstanding() =>
        Builders<SubscriptionFinancialDocument>.Filter.And(
            Builders<SubscriptionFinancialDocument>.Filter.Eq(
                document => document.Delivery.EmailedAtUtc,
                null),
            Builders<SubscriptionFinancialDocument>.Filter.Eq(
                document => document.Delivery.MailRequestedAtUtc,
                null),
            Builders<SubscriptionFinancialDocument>.Filter.In(
                document => document.Delivery.State,
                new[]
                {
                    FinancialDocumentDeliveryState.Pending,
                    FinancialDocumentDeliveryState.Generated
                }));

    public async Task<bool> TryRecordEmailAsync(
        string tenantId,
        string documentId,
        DateTime emailedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await Collection(tenantId).UpdateOneAsync(
            Builders<SubscriptionFinancialDocument>.Filter.And(
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.TenantId,
                    tenantId),
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.ItemId,
                    documentId),
                // Keyed on the state rather than on the timestamp being null, so two workers that
                // both published cannot both record it — which is what stops a second email being
                // treated as the first.
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.Delivery.State,
                    FinancialDocumentDeliveryState.Generated)),
            Builders<SubscriptionFinancialDocument>.Update
                .Set(document => document.Delivery.EmailedAtUtc, emailedAtUtc)
                .Set(
                    document => document.Delivery.State,
                    FinancialDocumentDeliveryState.Delivered)
                .Set(document => document.Delivery.LastErrorCode, null)
                .Set(document => document.LastUpdatedDateUtc, emailedAtUtc),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task RecordDeliveryFailureAsync(
        string tenantId,
        string documentId,
        string errorCode,
        int maximumAttempts,
        DateTime attemptedAtUtc,
        CancellationToken cancellationToken)
    {
        var document = await GetAsync(tenantId, documentId, cancellationToken);
        if (document is null)
        {
            return;
        }

        var attempts = document.Delivery.AttemptCount + 1;
        var update = Builders<SubscriptionFinancialDocument>.Update
            .Set(item => item.Delivery.AttemptCount, attempts)
            .Set(item => item.Delivery.LastErrorCode, errorCode)
            .Set(item => item.Delivery.LastAttemptAtUtc, attemptedAtUtc)
            .Set(item => item.LastUpdatedDateUtc, attemptedAtUtc);

        if (attempts >= maximumAttempts)
        {
            // Stops retrying, and says so in the state rather than only in a counter. An operator
            // looking for documents that never reached anybody should not have to know what the
            // configured attempt limit is to find them.
            update = update.Set(
                item => item.Delivery.State,
                FinancialDocumentDeliveryState.Abandoned);
        }

        await Collection(tenantId).UpdateOneAsync(
            Builders<SubscriptionFinancialDocument>.Filter.And(
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    item => item.TenantId,
                    tenantId),
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    item => item.ItemId,
                    documentId)),
            update,
            cancellationToken: cancellationToken);
    }

    public async Task TrySetRefundStatusAsync(
        string tenantId,
        string documentId,
        FinancialDocumentStatus status,
        CancellationToken cancellationToken) =>
        await Collection(tenantId).UpdateOneAsync(
            Builders<SubscriptionFinancialDocument>.Filter.And(
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.TenantId,
                    tenantId),
                Builders<SubscriptionFinancialDocument>.Filter.Eq(
                    document => document.ItemId,
                    documentId),
                // Never walks back from fully refunded to partially. A late-arriving partial refund
                // notification must not undo the record of a full one.
                Builders<SubscriptionFinancialDocument>.Filter.Ne(
                    document => document.Status,
                    FinancialDocumentStatus.Refunded)),
            Builders<SubscriptionFinancialDocument>.Update
                .Set(document => document.Status, status)
                .Set(document => document.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId))
        {
            return;
        }

        await Collection(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateFinancialDocumentIndexes(),
            cancellationToken);

        _indexed.TryAdd(tenantId, 0);
    }

    private IMongoCollection<SubscriptionFinancialDocument> Collection(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionFinancialDocument>(
            _db,
            tenantId,
            SubscriptionCollections.FinancialDocuments);
}
