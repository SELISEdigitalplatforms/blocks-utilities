using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// Reads the platform's own tenant registry.
/// </summary>
/// <remarks>
/// The registry lives in the root database rather than any tenant's, which is what makes it
/// readable from background work: it is addressed by connection string and database name
/// directly, so unlike every other read in this module it needs no ambient tenant to resolve
/// first. The same door <c>ProjectRepository</c> in blocks-os uses for its own root-scoped reads.
/// <para>
/// Projected through <see cref="BsonDocument"/> rather than the platform's tenant type. Only the
/// identifier is wanted, and reading one field keeps this from breaking the day a field is added
/// to a document this module does not own.
/// </para>
/// </remarks>
public sealed class RootDatabaseTenantSource : ISubscriptionTenantSource
{
    /// <summary>The registry's collection, as blocks-os names it.</summary>
    private const string TenantCollection = "Tenants";

    /// <summary>Used only when the secret carries no root database name.</summary>
    private const string FallbackRootDatabase = "BlocksRootDb";

    private const string TenantIdField = "TenantId";

    private readonly IDbContextProvider _dbContextProvider;
    private readonly IBlocksSecret _secret;

    public RootDatabaseTenantSource(
        IDbContextProvider dbContextProvider,
        IBlocksSecret secret)
    {
        _dbContextProvider = dbContextProvider;
        _secret = secret;
    }

    public async Task<IReadOnlyList<string>> ListTenantIdsAsync(
        CancellationToken cancellationToken)
    {
        var rootDatabase = string.IsNullOrWhiteSpace(_secret.RootDatabaseName)
            ? FallbackRootDatabase
            : _secret.RootDatabaseName;

        var tenants = _dbContextProvider
            .GetDatabase(_secret.DatabaseConnectionString, rootDatabase)
            .GetCollection<BsonDocument>(TenantCollection);

        var documents = await tenants
            .Find(Builders<BsonDocument>.Filter.Exists(TenantIdField))
            .Project(Builders<BsonDocument>.Projection.Include(TenantIdField))
            .ToListAsync(cancellationToken);

        return documents
            .Select(document => document.GetValue(TenantIdField, BsonNull.Value))
            .Where(value => value.IsString && !string.IsNullOrWhiteSpace(value.AsString))
            .Select(value => value.AsString)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
