using DomainService.Migration.Entities;

namespace DomainService.Migration.Services
{
    public interface IMigrationRepository
    {
        Task<string> CreateMigrationTrackerAsync(MigrationTracker migrationTracker);
        Task<MigrationTracker?> GetMigrationTrackerAsync(string trackerId);
        Task<bool> UpdateServiceStatusAsync(string trackerId, MigrationServiceNames serviceName, bool isCompleted, string? errorMessage = null);
        Task<List<MigrationTracker>> GetMigrationsByProjectKeysAsync(List<string> projectKeys);
        Task<List<MigrationTracker>> GetMigrationsByTenantGroupIdAsync(string tenantGroupId);
        Task<(int totalDocuments, int migratedDocuments)> MigrateCollectionAsync(string sourceProjectKey, string targetProjectKey, string collectionName, bool shouldOverwriteExistingData);
        Task<bool> CleanupCollectionAsync(string projectKey, string collectionName);
        Task<bool> MigrateDocumentsAsync(string targetProjectKey, string collectionName);
    }
}