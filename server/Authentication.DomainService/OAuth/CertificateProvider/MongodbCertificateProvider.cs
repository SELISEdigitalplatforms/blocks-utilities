using Blocks.Genesis;
using DomainService.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainService.OAuth
{
    public class MongodbCertificateProvider : ICertificateProvider
    {
        private readonly ILogger _logger;
        private readonly IMongoDatabase _database;

        public MongodbCertificateProvider(ILogger logger, IBlocksSecret blocksSecret)
        {
            _logger = logger;
            _database = new MongoClient(blocksSecret.DatabaseConnectionString).GetDatabase(blocksSecret.RootDatabaseName);
        }

        public async Task<byte[]> GetCertificateAsync(string key)
        {
            try
            {
                var certificate = await _database.GetCollection<TenantCertificate>("TenantCertificates")
                    .Find(x => x.Key == key)
                    .FirstOrDefaultAsync();

                return Convert.FromBase64String(certificate?.Value ?? string.Empty);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error retrieving certificate from file system");
                return Array.Empty<byte>();
            }
        }
    }
}
