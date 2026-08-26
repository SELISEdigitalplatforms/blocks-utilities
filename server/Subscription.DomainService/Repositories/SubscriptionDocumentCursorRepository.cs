using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionDocumentCursorRepository : ISubscriptionDocumentCursorRepository
{
    private const string InstantField = "ReadUpToUtc";

    private const string AfterIdField = "AfterId";

    /// <summary>
    /// How many times the write re-asks its question.
    /// </summary>
    /// <remarks>
    /// Two is enough: after one round a document certainly exists, so the conditional update either
    /// applies or correctly declines. The third is slack against nothing in particular, and the bound
    /// exists so a mark can never spin — a sweep must not be able to hang on its own bookkeeping.
    /// </remarks>
    private const int MaximumAttempts = 3;

    private readonly IDbContextProvider _db;

    public SubscriptionDocumentCursorRepository(IDbContextProvider db) => _db = db;

    public async Task<FinancialDocumentSweepMark?> GetAsync(
        string tenantId,
        string cursorName,
        CancellationToken cancellationToken)
    {
        var stored = await Collection(tenantId)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", cursorName))
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null ||
            !stored.TryGetValue(InstantField, out var instant) ||
            !instant.IsValidDateTime)
        {
            return null;
        }

        return new FinancialDocumentSweepMark(
            DateTime.SpecifyKind(instant.ToUniversalTime(), DateTimeKind.Utc),
            stored.TryGetValue(AfterIdField, out var afterId) && afterId.IsString
                ? afterId.AsString
                : null);
    }

    /// <summary>
    /// Writes the mark, and only ever forwards.
    /// </summary>
    /// <remarks>
    /// Two operations rather than one, because the condition and the upsert cannot be combined: an
    /// upsert filtered on "the stored mark is older" fails to match when it is newer, and then tries to
    /// insert a second document under the same <c>_id</c>. So the conditional update runs first and an
    /// insert-only upsert fills in the very first mark.
    /// <para>
    /// And then <em>round again</em>, because those two operations are not one atomic step. Two workers
    /// starting a tenant from scratch both find nothing to update and both go on to insert; whichever
    /// loses inserts nothing and would otherwise walk away leaving the other's mark standing — even
    /// when the other one is <em>behind</em> it. The loop turns that into the ordinary case: a document
    /// now exists, so the conditional update applies and either moves the mark forward or correctly
    /// declines. It converges in two passes and is bounded only against a concurrent delete that
    /// nothing performs.
    /// </para>
    /// <para>
    /// Deliberately not <c>$max</c>, which was enough while a mark was one instant and is not now: the
    /// comparison is over the pair, and <c>$max</c> on the instant alone would refuse a mark that
    /// advanced within an instant — which is exactly how a page of records sharing one instant makes
    /// progress.
    /// </para>
    /// </remarks>
    public async Task SetAsync(
        string tenantId,
        string cursorName,
        FinancialDocumentSweepMark mark,
        CancellationToken cancellationToken)
    {
        var identity = Builders<BsonDocument>.Filter.Eq("_id", cursorName);
        var afterId = (BsonValue)(mark.AfterId is null ? BsonNull.Value : mark.AfterId);

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var moved = await Collection(tenantId).UpdateOneAsync(
                Builders<BsonDocument>.Filter.And(
                    identity,
                    Builders<BsonDocument>.Filter.Or(
                        Builders<BsonDocument>.Filter.Lt(InstantField, mark.ReadUpToUtc),
                        Builders<BsonDocument>.Filter.And(
                            Builders<BsonDocument>.Filter.Eq(InstantField, mark.ReadUpToUtc),
                            Builders<BsonDocument>.Filter.Lt(AfterIdField, afterId)))),
                Builders<BsonDocument>.Update
                    .Set(InstantField, mark.ReadUpToUtc)
                    .Set(AfterIdField, afterId),
                cancellationToken: cancellationToken);

            if (moved.MatchedCount > 0)
            {
                return;
            }

            // No document matched, which means either there is no mark yet or the stored one is
            // already at or beyond this. The insert-only upsert settles the first case; if it inserts
            // nothing, a document exists and the loop re-asks the conditional question against it.
            var inserted = await Collection(tenantId).UpdateOneAsync(
                identity,
                Builders<BsonDocument>.Update
                    .SetOnInsert(InstantField, mark.ReadUpToUtc)
                    .SetOnInsert(AfterIdField, afterId),
                new UpdateOptions { IsUpsert = true },
                cancellationToken);

            if (inserted.UpsertedId is not null)
            {
                return;
            }
        }
    }

    private IMongoCollection<BsonDocument> Collection(string tenantId) =>
        SubscriptionCollections.Of<BsonDocument>(
            _db,
            tenantId,
            SubscriptionCollections.DocumentCursors);
}
