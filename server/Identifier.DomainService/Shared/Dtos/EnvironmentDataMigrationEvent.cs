namespace DomainService.Dtos
{
    public class EnvironmentDataMigrationEvent
    {
        public required string ProjectKey { get; set; }
        public required string TargetedProjectKey { get; set; }
        public bool ShouldOverWriteExistingData { get; set; } = false;
        public string? TrackerId { get; set; }
    }
}