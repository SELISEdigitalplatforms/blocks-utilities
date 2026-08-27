using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;

namespace XUnitTest.Integration;

/// <summary>
/// A reusable xUnit fixture that connects to a locally running MongoDB and
/// hands out a throwaway database that is unique to this test run. The database
/// name is derived from a <see cref="Guid"/> so concurrent test sessions never
/// clash, and the whole database is dropped on <see cref="Dispose"/>. No
/// pre-existing database is ever touched.
///
/// If mongod is not reachable the fixture throws on construction so the tests
/// that depend on it fail loudly rather than silently skipping.
/// </summary>
public sealed class MongoIntegrationFixture : IDisposable
{
    private const string DefaultConnectionString = "mongodb://localhost:27017";

    /// <summary>
    /// Where the harness looks for a database, defaulting to a local mongod.
    /// </summary>
    /// <remarks>
    /// Set <c>BLOCKS_IT_MONGO</c> to run against a shared server — CI, or a developer machine
    /// with no local mongod. It is read from the environment rather than committed because it
    /// carries credentials. Pointing it at a shared server is safe: the fixture only ever
    /// creates its own uniquely named database and drops only that one.
    /// </remarks>
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("BLOCKS_IT_MONGO") is { Length: > 0 } configured
            ? configured
            : DefaultConnectionString;

    public MongoIntegrationFixture()
    {
        DatabaseName = "blocks_utilities_it_" + Guid.NewGuid().ToString("N");

        var settings = MongoClientSettings.FromConnectionString(ConnectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        settings.ConnectTimeout = TimeSpan.FromSeconds(5);
        Client = new MongoClient(settings);

        try
        {
            Client.GetDatabase("admin").RunCommand<BsonDocument>(
                new BsonDocument("ping", 1));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                // Deliberately not the connection string: it may carry credentials, and this
                // message reaches test output and CI logs.
                "MongoDB is not reachable. The integration test harness requires a running " +
                "mongod, or BLOCKS_IT_MONGO pointing at one.",
                exception);
        }

        Database = Client.GetDatabase(DatabaseName);
        DbContextProvider = new SingleDatabaseContextProvider(Database);

        RootDatabaseName = DatabaseName + "_root";
        RootDatabase = Client.GetDatabase(RootDatabaseName);
        SplitDbContextProvider = new SplitDatabaseContextProvider(
            Database,
            RootDatabase,
            RootDatabaseName);
    }

    public string DatabaseName { get; }

    /// <summary>
    /// A physically separate database standing in for <c>BlocksRootDb</c>.
    /// </summary>
    /// <remarks>
    /// Separate because one shared database cannot prove where a document was written. The scheduling
    /// layer and the tenant's own records live apart in production, and a test that maps both onto one
    /// database would pass just as happily if a financial document were written into the root — which
    /// is one of the failures this harness exists to catch.
    /// </remarks>
    public string RootDatabaseName { get; }

    public IMongoDatabase RootDatabase { get; }

    public IMongoClient Client { get; }

    public IMongoDatabase Database { get; }

    /// <summary>
    /// An <see cref="IDbContextProvider"/> that resolves every tenant to the
    /// same throwaway database. Tenant isolation is still exercised because the
    /// repositories filter on the TenantId field inside each collection.
    /// </summary>
    public IDbContextProvider DbContextProvider { get; }

    /// <summary>
    /// Resolves the root database by name and everything else to the tenant database.
    /// </summary>
    /// <remarks>
    /// Use this wherever a test spans both layers — the queue in the root database and the tenant's
    /// own records. <see cref="DbContextProvider"/> collapses them onto one database, which is fine
    /// for a test about one collection and useless for a test about which database a write landed in.
    /// </remarks>
    public IDbContextProvider SplitDbContextProvider { get; }

    public IMongoCollection<T> Collection<T>(string name) =>
        Database.GetCollection<T>(name);

    /// <summary>Generates a fresh tenant id so each test is isolated.</summary>
    public static string NewTenantId() => Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        Client.DropDatabase(DatabaseName);
        Client.DropDatabase(RootDatabaseName);
    }

    /// <summary>
    /// Two databases, told apart by the name the caller asks for.
    /// </summary>
    /// <remarks>
    /// The queue and the worker registry ask for the root database by connection string and name,
    /// exactly as they do in production; everything tenant-scoped resolves by tenant id and gets the
    /// tenant database. So a write that goes to the wrong one lands in a collection the test can look
    /// at and fail on.
    /// </remarks>
    private sealed class SplitDatabaseContextProvider : IDbContextProvider
    {
        private readonly IMongoDatabase _tenant;
        private readonly IMongoDatabase _root;
        private readonly string _rootName;

        public SplitDatabaseContextProvider(
            IMongoDatabase tenant,
            IMongoDatabase root,
            string rootName)
        {
            _tenant = tenant;
            _root = root;
            _rootName = rootName;
        }

        public IMongoDatabase GetDatabase(string tenantId) => _tenant;

        public IMongoDatabase GetDatabase() => _tenant;

        public IMongoDatabase GetDatabase(
            string connectionString,
            string databaseName,
            bool isCacheRefreshed = false) =>
            string.Equals(databaseName, _rootName, StringComparison.Ordinal)
                ? _root
                : _tenant;

        public IMongoCollection<T> GetCollection<T>(string collectionName) =>
            _tenant.GetCollection<T>(collectionName);

        public IMongoCollection<T> GetCollection<T>(
            string tenantId,
            string collectionName) =>
            _tenant.GetCollection<T>(collectionName);
    }

    private sealed class SingleDatabaseContextProvider : IDbContextProvider
    {
        private readonly IMongoDatabase _database;

        public SingleDatabaseContextProvider(IMongoDatabase database) =>
            _database = database;

        public IMongoDatabase GetDatabase(string tenantId) => _database;

        public IMongoDatabase GetDatabase() => _database;

        public IMongoDatabase GetDatabase(
            string connectionString,
            string databaseName,
            bool isCacheRefreshed = false) => _database;

        public IMongoCollection<T> GetCollection<T>(string collectionName) =>
            _database.GetCollection<T>(collectionName);

        public IMongoCollection<T> GetCollection<T>(
            string tenantId,
            string collectionName) =>
            _database.GetCollection<T>(collectionName);
    }
}

/// <summary>
/// Shared xUnit collection so every MongoDB integration test class reuses one
/// fixture (one throwaway database) and runs serially, avoiding cross-test
/// interference on shared collections.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MongoIntegrationCollection :
    ICollectionFixture<MongoIntegrationFixture>
{
    public const string Name = "mongo-integration";
}
