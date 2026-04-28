using Blocks.Genesis;
using DomainService.Migration;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Worker.Consumers.Identifier
{
    public class DataCleanupConsumer : IConsumer<PublishScheduleCommand>
    {
        private readonly IMigrationService _migrationService;
        private readonly ILogger<DataCleanupConsumer> _logger;

        public DataCleanupConsumer(IMigrationService migrationService, ILogger<DataCleanupConsumer> logger)
        {
            _migrationService = migrationService;
            _logger = logger;
        }

        public async Task Consume(PublishScheduleCommand request)
        {
            DataCleanupRequest? dataCleanupRequest = null;
            try
            {
                if (string.IsNullOrWhiteSpace(request.Payload))
                {
                    _logger.LogError("Payload is null or empty.");
                    return;
                }

                dataCleanupRequest = JsonSerializer.Deserialize<DataCleanupRequest>(request.Payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize payload to DataCleanupRequest. Payload: {Payload}", request.Payload);
                return;
            }

            if (dataCleanupRequest == null || string.IsNullOrEmpty(dataCleanupRequest.ProjectKey))
            {
                _logger.LogError("Invalid DataCleanupRequest received. Payload: {Payload}", request.Payload);
                return;
            }

            await _migrationService.DataCleanupAsync(dataCleanupRequest);
        }
    }
}
