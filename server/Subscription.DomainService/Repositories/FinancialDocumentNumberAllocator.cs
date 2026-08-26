using System.Globalization;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// Allocates document numbers with one atomic increment per number.
/// </summary>
/// <remarks>
/// <c>findAndModify</c> with <c>$inc</c> and an upsert is the whole mechanism, and it is enough:
/// MongoDB serialises concurrent updates to a single document, so two workers issuing invoices in the
/// same millisecond get consecutive numbers rather than the same one. No lock, no read-then-write, and
/// no reliance on the workers agreeing about anything.
/// <para>
/// One counter per tenant per prefix per year. The year is in the counter's identity rather than
/// filtered on, so January resets to 1 without anybody running a job — and a tenant's numbering is
/// isolated from every other tenant's by living in their own database.
/// </para>
/// </remarks>
public sealed class FinancialDocumentNumberAllocator : IFinancialDocumentNumberAllocator
{
    /// <summary>
    /// Six digits, zero-padded — <c>INV-2026-000123</c>. Wider numbers are not truncated, they simply
    /// stop being padded, so a tenant issuing a millionth invoice gets an ugly number rather than a
    /// duplicate one.
    /// </summary>
    private const string SequenceFormat = "D6";

    private readonly IDbContextProvider _db;

    public FinancialDocumentNumberAllocator(IDbContextProvider db) => _db = db;

    public async Task<string> AllocateAsync(
        string tenantId,
        FinancialDocumentType documentType,
        int year,
        CancellationToken cancellationToken)
    {
        var prefix = PrefixFor(documentType);
        var counterId = $"{prefix}:{year.ToString(CultureInfo.InvariantCulture)}";

        var counter = await Collection(tenantId).FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("_id", counterId),
            Builders<BsonDocument>.Update.Inc("CurrentNumber", 1L),
            new FindOneAndUpdateOptions<BsonDocument>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        var next = counter["CurrentNumber"].ToInt64();

        return $"{prefix}-{year.ToString(CultureInfo.InvariantCulture)}-" +
            next.ToString(SequenceFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The prefix each kind of document is numbered under.
    /// </summary>
    /// <remarks>
    /// Trial invoices share the invoice sequence rather than getting a third. They are invoices — a
    /// zero-total one — and a subscriber whose first document is <c>INV-2026-000001</c> and whose
    /// second is <c>INV-2026-000002</c> can see they have all of them. A separate series would make
    /// their invoice numbering start at 1 twice.
    /// </remarks>
    private static string PrefixFor(FinancialDocumentType documentType) =>
        documentType == FinancialDocumentType.CreditNote ? "CRN" : "INV";

    private IMongoCollection<BsonDocument> Collection(string tenantId) =>
        SubscriptionCollections.Of<BsonDocument>(
            _db,
            tenantId,
            SubscriptionCollections.DocumentNumbers);
}
