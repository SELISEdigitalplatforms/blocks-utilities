using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Migration;
using DomainService.Migration.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace Worker.Consumers.Identifier
{
    public class MigrationCompletionConsumer : IConsumer<MigrationCompletionEvent>
    {
        private readonly IMigrationRepository _migrationRepository;
        private readonly ILogger<MigrationCompletionConsumer> _logger;
        private readonly IMigrationService _migrationService;

        public MigrationCompletionConsumer(
            IMigrationRepository migrationRepository,
            ILogger<MigrationCompletionConsumer> logger,
            IMigrationService migrationService)
        {
            _migrationRepository = migrationRepository;
            _logger = logger;
            _migrationService = migrationService;
        }

        public async Task Consume(MigrationCompletionEvent migrationCompletion)
        {
            try
            {
                _logger.LogInformation(
                    "Processing migration completion for TrackerId: {TrackerId}, Service: {ServiceName}, Success: {IsSuccess}",
                    migrationCompletion.TrackerId,
                    migrationCompletion.ServiceName,
                    migrationCompletion.IsSuccess);

                // Parse the service name to enum
                if (!Enum.TryParse<MigrationServiceNames>(migrationCompletion.ServiceName, true, out var serviceName))
                {
                    _logger.LogError(
                        "Invalid service name '{ServiceName}' for TrackerId: {TrackerId}",
                        migrationCompletion.ServiceName,
                        migrationCompletion.TrackerId);
                    return;
                }

                // Update the migration tracker with completion status
                var updateResult = await _migrationRepository.UpdateServiceStatusAsync(
                    migrationCompletion.TrackerId,
                    serviceName,
                    migrationCompletion.IsSuccess,
                    migrationCompletion.ErrorMessage);

                var tracker = await _migrationRepository.GetMigrationTrackerAsync(migrationCompletion.TrackerId);
                if (tracker == null)
                {
                    _logger.LogError(
                        "Migration tracker not found for TrackerId: {TrackerId}",
                        migrationCompletion.TrackerId);
                    return;
                }

                await _migrationService.NotifyServiceDataMigrationProgress(migrationCompletion.IsSuccess, tracker.ProjectKey, tracker.TargetedProjectKey);

                if (updateResult)
                {
                    _logger.LogInformation(
                        "Successfully updated migration status for TrackerId: {TrackerId}, Service: {ServiceName}",
                        migrationCompletion.TrackerId,
                        migrationCompletion.ServiceName);

                    if (tracker != null && _migrationService.AreAllServicesCompleted(tracker))
                    {
                        _logger.LogInformation(
                            "All services completed for TrackerId: {TrackerId}. Sending final migration completion notification.",
                            migrationCompletion.TrackerId);
                        await _migrationService.NotifyDataMigrationEvent(true, tracker.ProjectKey, tracker.TargetedProjectKey);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to update migration status for TrackerId: {TrackerId}, Service: {ServiceName}. Tracker may not exist.",
                        migrationCompletion.TrackerId,
                        migrationCompletion.ServiceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error processing migration completion for TrackerId: {TrackerId}, Service: {ServiceName}",
                    migrationCompletion.TrackerId,
                    migrationCompletion.ServiceName);

                // Re-throw to allow message queue retry logic to handle
                throw;
            }
        }
    }
}
