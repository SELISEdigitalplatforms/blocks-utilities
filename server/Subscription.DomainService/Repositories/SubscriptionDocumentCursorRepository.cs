using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionDocumentCursorRepository : ISubscriptionDocumentCursorRepository
{
    private const string InstantField = "ReadUpToUtc";

    private const string AfterIdField = "AfterId";

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

        var update = Builders<BsonDocument>.Update
            .Set(InstantField, mark.ReadUpToUtc)
            .Set(AfterIdField, afterId);

        var moved = await Collection(tenantId).UpdateOneAsync(
            Builders<BsonDocument>.Filter.And(
                identity,
                Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Lt(InstantField, mark.ReadUpToUtc),
                    Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Eq(InstantField, mark.ReadUpToUtc),
                        Builders<BsonDocument>.Filter.Lt(AfterIdField, afterId)))),
            update,
            cancellationToken: cancellationToken);

        if (moved.MatchedCount > 0)
        {
            return;
        }

        // Either the stored mark is already at or beyond this one — two workers swept the same tenant
        // and the other got further — or there is no mark yet. Only the second case is a write, and
        // $setOnInsert is what tells them apart without a read.
        await Collection(tenantId).UpdateOneAsync(
            identity,
            Builders<BsonDocument>.Update
                .SetOnInsert(InstantField, mark.ReadUpToUtc)
                .SetOnInsert(AfterIdField, afterId),
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    private IMongoCollection<BsonDocument> Collection(string tenantId) =>
        SubscriptionCollections.Of<BsonDocument>(
            _db,
            tenantId,
            SubscriptionCollections.DocumentCursors);
}
