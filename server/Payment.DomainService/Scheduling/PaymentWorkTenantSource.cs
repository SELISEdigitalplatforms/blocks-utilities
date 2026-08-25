using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Payment.DomainService.Scheduling;

/// <summary>
/// Reads the platform's tenant registry, so background work knows whose payments to look at.
/// </summary>
/// <remarks>
/// A second reader of the same registry the subscription module reads, and deliberately so: the
/// dependency runs subscription → payment, so this module cannot borrow that one without inverting
/// it. Duplicating a nine-line projection is a smaller price than a payment module that cannot be
/// deployed without a subscription module.
/// <para>
/// Projected through <see cref="BsonDocument"/> rather than the platform's tenant type. Only the
/// identifier is wanted, and reading one field keeps this from breaking the day a field is added to
/// a document this module does not own.
/// </para>
/// </remarks>
public sealed class PaymentWorkTenantSource : IPaymentWorkTenantSource
{
    private const string TenantCollection = "Tenants";
    private const string FallbackRootDatabase = "BlocksRootDb";
    private const string TenantIdField = "TenantId";

    private readonly IDbContextProvider _dbContextProvider;
    private readonly IBlocksSecret _secret;

    public PaymentWorkTenantSource(IDbContextProvider dbContextProvider, IBlocksSecret secret)
    {
        _dbContextProvider = dbContextProvider;
        _secret = secret;
    }

    public async Task<IReadOnlyList<string>> ListTenantIdsAsync(CancellationToken cancellationToken)
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

public interface IPaymentWorkTenantSource
{
    Task<IReadOnlyList<string>> ListTenantIdsAsync(CancellationToken cancellationToken);
}
