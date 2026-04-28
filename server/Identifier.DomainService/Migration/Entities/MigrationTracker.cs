using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Migration.Entities
{
    [BsonIgnoreExtraElements]
    public class MigrationTracker : BaseEntity
    {
        public required string ProjectKey { get; set; }
        public required string TargetedProjectKey { get; set; }
        public required string TenantGroupId { get; set; }
        public DateTime MigrationStartedAt { get; set; } = DateTime.UtcNow;
        public string? ErrorMessage { get; set; }

        // Individual service properties
        public ServiceMigrationStatus? Authentication { get; set; }
        public ServiceMigrationStatus? IAM { get; set; }
        public ServiceMigrationStatus? MFA { get; set; }
        public ServiceMigrationStatus? CAPTCHA { get; set; }
        public ServiceMigrationStatus? Email { get; set; }
        public ServiceMigrationStatus? DataGateway { get; set; }
        public ServiceMigrationStatus? Notifications { get; set; }
        public ServiceMigrationStatus? Storage { get; set; }
        public ServiceMigrationStatus? LanguageService { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class ServiceMigrationStatus
    {
        public bool ShouldOverWriteExistingData { get; set; } = false;
        public bool IsCompleted { get; set; } = false;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public string? QueueName { get; set; }
    }
}