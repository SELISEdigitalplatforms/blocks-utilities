using DomainService.Migration.Entities;

namespace DomainService.Migration
{
    public interface IMigrationService
    {
        Task<MigrationOtpGenerationResponse> Migrate(MigrationRequest request);
        Task<MigrationOtpVerificationResponse> VerifyAsync(MigrationVerifyOtpRequest request);
        Task<bool> NotifyDataMigrationProgress(bool response, string projectKey, string targetedProjectKey);
        Task<bool> NotifyEnvironmentDataMigration(bool response, string projectKey, string targetedProjectKey);
        Task<bool> NotifyServiceDataMigrationProgress(bool response, string projectKey, string targetedProjectKey);
        Task<bool> NotifyDataMigrationEvent(bool response, string projectKey, string targetedProjectKey);
        Task<bool> NotifyMigrationStarted(string projectKey, string targetedProjectKey);
        bool AreAllServicesCompleted(MigrationTracker tracker);
        Task MigrateEnvironmentDataAsync(string projectKey, string targetedProjectKey, bool shouldOverwriteExistingData, string trackerId);
        Task<bool> DataCleanupAsync(DataCleanupRequest request);
    }
}