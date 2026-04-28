using Blocks.Genesis;
using DomainService.Migration.Entities;
using DomainService.Shared;
using MongoDB.Driver;
using MongoDB.Bson;

namespace DomainService.Migration.Services
{
    public class MigrationRepository : IMigrationRepository
    {
        private readonly IMongoCollection<MigrationTracker> _collection;
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;

        public MigrationRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _collection = dbContextProvider.GetCollection<MigrationTracker>(IdentifierConstants.MigrationTrackerCollectionName);
            _blocksSecret = blocksSecret;
        }

        public async Task<string> CreateMigrationTrackerAsync(MigrationTracker migrationTracker)
        {
            await _collection.InsertOneAsync(migrationTracker);
            return migrationTracker.ItemId;
        }

        public async Task<MigrationTracker?> GetMigrationTrackerAsync(string trackerId)
        {
            var filter = Builders<MigrationTracker>.Filter.Eq(m => m.ItemId, trackerId);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateServiceStatusAsync(string trackerId, MigrationServiceNames serviceName, bool isCompleted, string? errorMessage = null)
        {
            var filter = Builders<MigrationTracker>.Filter.Eq(m => m.ItemId, trackerId);
            
            var propertyName = GetServicePropertyName(serviceName);
            var update = Builders<MigrationTracker>.Update
                .Set($"{propertyName}.IsCompleted", isCompleted)
                .Set($"{propertyName}.CompletedAt", isCompleted ? DateTime.UtcNow : (DateTime?)null)
                .Set($"{propertyName}.ErrorMessage", errorMessage)
                .Set(m => m.LastUpdatedDate, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<List<MigrationTracker>> GetMigrationsByProjectKeysAsync(List<string> projectKeys)
        {
            var filter = Builders<MigrationTracker>.Filter.Or(
                Builders<MigrationTracker>.Filter.In(m => m.ProjectKey, projectKeys),
                Builders<MigrationTracker>.Filter.In(m => m.TargetedProjectKey, projectKeys)
            );

            var trackers = await _collection.Find(filter)
                .SortByDescending(m => m.CreatedDate)
                .ToListAsync();

            // Filter to only include trackers with at least one incomplete service
            return trackers.Where(HasIncompleteServices).ToList();
        }

        public async Task<List<MigrationTracker>> GetMigrationsByTenantGroupIdAsync(string tenantGroupId)
        {
            var filter = Builders<MigrationTracker>.Filter.Eq(m => m.TenantGroupId, tenantGroupId);

            var trackers = await _collection.Find(filter)
                .SortByDescending(m => m.CreatedDate)
                .ToListAsync();

            // Filter to only include trackers with at least one incomplete service
            return trackers.Where(HasIncompleteServices).ToList();
        }

        private static string GetServicePropertyName(MigrationServiceNames serviceName)
        {
            return serviceName switch
            {
                MigrationServiceNames.Authentication => nameof(MigrationTracker.Authentication),
                MigrationServiceNames.IAM => nameof(MigrationTracker.IAM),
                MigrationServiceNames.MFA => nameof(MigrationTracker.MFA),
                MigrationServiceNames.CAPTCHA => nameof(MigrationTracker.CAPTCHA),
                MigrationServiceNames.Email => nameof(MigrationTracker.Email),
                MigrationServiceNames.DataGateway => nameof(MigrationTracker.DataGateway),
                MigrationServiceNames.Notifications => nameof(MigrationTracker.Notifications),
                MigrationServiceNames.Storage => nameof(MigrationTracker.Storage),
                MigrationServiceNames.Language => nameof(MigrationTracker.LanguageService),
                _ => throw new ArgumentException($"Unknown service name: {serviceName}")
            };
        }

        private static bool HasIncompleteServices(MigrationTracker tracker)
        {
            return (tracker.Authentication != null && !tracker.Authentication.IsCompleted) ||
                   (tracker.IAM != null && !tracker.IAM.IsCompleted) ||
                   (tracker.MFA != null && !tracker.MFA.IsCompleted) ||
                   (tracker.CAPTCHA != null && !tracker.CAPTCHA.IsCompleted) ||
                   (tracker.Email != null && !tracker.Email.IsCompleted) ||
                   (tracker.DataGateway != null && !tracker.DataGateway.IsCompleted) ||
                   (tracker.Notifications != null && !tracker.Notifications.IsCompleted) ||
                   (tracker.Storage != null && !tracker.Storage.IsCompleted) ||
                   (tracker.LanguageService != null && !tracker.LanguageService.IsCompleted);
        }

        public async Task<(int totalDocuments, int migratedDocuments)> MigrateCollectionAsync(string sourceProjectKey, string targetProjectKey,
            string collectionName, bool shouldOverwriteExistingData)
        {
            // Get source database and collection
            var sourceDatabase = _dbContextProvider.GetDatabase(sourceProjectKey);
            var sourceCollection = sourceDatabase.GetCollection<BsonDocument>(collectionName);

            // Get target database and collection
            var targetDatabase = _dbContextProvider.GetDatabase(targetProjectKey);
            var targetCollection = targetDatabase.GetCollection<BsonDocument>(collectionName);

            // Check if source collection has any documents
            var sourceCount = await sourceCollection.CountDocumentsAsync(new BsonDocument());
            if (sourceCount == 0)
            {
                return (0, 0); // No data to migrate
            }

            const int batchSize = 1000; // Process documents in batches to optimize memory usage
            var totalProcessed = 0;
            var totalDocuments = (int)sourceCount;

            if (shouldOverwriteExistingData)
            {
                // Process documents in batches for upsert operations
                using var cursor = await sourceCollection.FindAsync(new BsonDocument());

                while (await cursor.MoveNextAsync())
                {
                    var batch = cursor.Current.Take(batchSize).ToList();
                    if (!batch.Any()) break;

                    var bulkOps = batch.Select(document =>
                        new ReplaceOneModel<BsonDocument>(
                            Builders<BsonDocument>.Filter.Eq("_id", document["_id"]),
                            document)
                        {
                            IsUpsert = true
                        } as WriteModel<BsonDocument>).ToList();

                    if (bulkOps.Any())
                    {
                        await targetCollection.BulkWriteAsync(bulkOps, new BulkWriteOptions { IsOrdered = false });
                        totalProcessed += bulkOps.Count;
                    }
                }
            }
            else
            {
                // Insert-only mode using UpdateOneModel with $setOnInsert
                // This approach is more explicit and avoids exceptions
                using var cursor = await sourceCollection.FindAsync(new BsonDocument());

                while (await cursor.MoveNextAsync())
                {
                    var batch = cursor.Current.Take(batchSize).ToList();
                    if (!batch.Any()) break;

                    var bulkOps = batch.Select(document =>
                    {
                        var filter = Builders<BsonDocument>.Filter.Eq("_id", document["_id"]);

                        // Build update with $setOnInsert for all fields
                        var updateBuilder = Builders<BsonDocument>.Update;
                        UpdateDefinition<BsonDocument>? update = null;

                        foreach (var element in document.Elements)
                        {
                            if (update == null)
                                update = updateBuilder.SetOnInsert(element.Name, element.Value);
                            else
                                update = update.SetOnInsert(element.Name, element.Value);
                        }

                        return new UpdateOneModel<BsonDocument>(filter, update!) { IsUpsert = true };
                    }).ToList();

                    if (bulkOps.Any())
                    {
                        var bulkResult = await targetCollection.BulkWriteAsync(bulkOps, new BulkWriteOptions { IsOrdered = false });
                        totalProcessed += bulkResult.Upserts.Count; // Count actual upserts (inserts in this case)
                    }
                }
            }

            return (totalDocuments, totalProcessed);
        }

        public async Task<bool> CleanupCollectionAsync(string projectKey, string collectionName)
        {
            try
            {
                var database = _dbContextProvider.GetDatabase(projectKey);
                var collection = database.GetCollection<BsonDocument>(collectionName);

                var deleteResult = await collection.DeleteManyAsync(new BsonDocument());
                return deleteResult.IsAcknowledged;
            }
            catch (Exception ex)
            {
                // Log the exception as needed
                Console.WriteLine($"Error during cleanup: {ex.Message}");
                return false;
            }
        }

        public async Task<bool>  MigrateDocumentsAsync(string targetProjectKey, string collectionName)
        {
            try
            {
                var sourceDatabase = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, "BlocksConfiguration");
                var targetDatabase = _dbContextProvider.GetDatabase(targetProjectKey);

                await CopyDocumentAsync(sourceDatabase, targetDatabase, collectionName);
                return true;
            }
            catch (Exception ex)
            {
                // Log the exception as needed
                Console.WriteLine($"Error during document migration: {ex.Message}");
                return false;
            }
        }
        private async Task CopyDocumentAsync(IMongoDatabase sourceDb, IMongoDatabase targetDb, string collectionName)
        {
            var sourceCollection = sourceDb.GetCollection<BsonDocument>(collectionName);
            var targetCollection = targetDb.GetCollection<BsonDocument>(collectionName);

            var sourceCount = await sourceCollection.CountDocumentsAsync(new BsonDocument());
            if (sourceCount == 0)
            {
                return;
            }

            const int batchSize = 1000;
            using var cursor = await sourceCollection.FindAsync(new BsonDocument());

            while (await cursor.MoveNextAsync())
            {
                var batch = cursor.Current.Take(batchSize).ToList();
                if (!batch.Any()) break;

                await targetCollection.InsertManyAsync(batch, new InsertManyOptions { IsOrdered = false });
            }
        }
    }
}