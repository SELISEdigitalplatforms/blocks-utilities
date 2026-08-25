using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionDocumentCursorRepository : ISubscriptionDocumentCursorRepository
{
    private const string Field = "ReadUpToUtc";

    private readonly IDbContextProvider _db;

    public SubscriptionDocumentCursorRepository(IDbContextProvider db) => _db = db;

    public async Task<DateTime?> GetAsync(
        string tenantId,
        string cursorName,
        CancellationToken cancellationToken)
    {
        var stored = await Collection(tenantId)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", cursorName))
            .FirstOrDefaultAsync(cancellationToken);

        return stored is not null && stored.TryGetValue(Field, out var value) && value.IsValidDateTime
            ? DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc)
            : null;
    }

    public async Task SetAsync(
        string tenantId,
        string cursorName,
        DateTime readUpToUtc,
        CancellationToken cancellationToken) =>
        // $max rather than $set, which is what makes the write monotonic without a read first: two
        // workers sweeping the same tenant converge on the furthest either of them reached instead of
        // taking turns dragging the mark backwards. Deliberately not a $set under a "stored mark is
        // older" filter, which is the same idea spelled as a bug — the filter would fail to match
        // when the stored mark is newer and the upsert would then try to insert a second document
        // under the same _id.
        await Collection(tenantId).UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", cursorName),
            Builders<BsonDocument>.Update.Max(Field, readUpToUtc),
            new UpdateOptions { IsUpsert = true },
            cancellationToken);

    private IMongoCollection<BsonDocument> Collection(string tenantId) =>
        SubscriptionCollections.Of<BsonDocument>(
            _db,
            tenantId,
            SubscriptionCollections.DocumentCursors);
}
