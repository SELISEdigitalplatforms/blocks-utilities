using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Migration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Worker.Consumers.Identifier
{
    public class EnvironmentDataMigrationEventConsumer : IConsumer<EnvironmentDataMigrationEvent>
    {
        private readonly ILogger<EnvironmentDataMigrationEventConsumer> _logger;
        private readonly IMigrationService _migrationService;

        public EnvironmentDataMigrationEventConsumer(
            ILogger<EnvironmentDataMigrationEventConsumer> logger,
            IMigrationService migrationService)
        {
            _logger = logger;
            _migrationService = migrationService;
        }

        public async Task Consume(EnvironmentDataMigrationEvent migrationEvent)
        {
            try
            {
                _logger.LogInformation(
                    "Starting environment data migration from ProjectKey: {ProjectKey} to TargetedProjectKey: {TargetedProjectKey}, OverwriteExistingData: {ShouldOverWriteExistingData}, TrackerId: {TrackerId}",
                    migrationEvent.ProjectKey,
                    migrationEvent.TargetedProjectKey,
                    migrationEvent.ShouldOverWriteExistingData,
                    migrationEvent.TrackerId);

                if (string.IsNullOrWhiteSpace(migrationEvent.TrackerId))
                {
                    _logger.LogError("TrackerId is required for environment data migration.");
                    return;
                }

                await _migrationService.MigrateEnvironmentDataAsync(
                    migrationEvent.ProjectKey,
                    migrationEvent.TargetedProjectKey,
                    migrationEvent.ShouldOverWriteExistingData,
                    migrationEvent.TrackerId);

                _logger.LogInformation(
                    "Completed environment data migration from ProjectKey: {ProjectKey} to TargetedProjectKey: {TargetedProjectKey}",
                    migrationEvent.ProjectKey,
                    migrationEvent.TargetedProjectKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during environment data migration for ProjectKey: {ProjectKey} to TargetedProjectKey: {TargetedProjectKey}",
                    migrationEvent.ProjectKey,
                    migrationEvent.TargetedProjectKey);
            }
        }
    }
}
