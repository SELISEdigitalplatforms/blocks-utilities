using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Resources;

namespace Worker.Consumers
{
    public class ResourceSetToPermissionMutationConsumer : IConsumer<ResourceSetToPermissionMutationEvent>
    {
        private readonly ILogger<ResourceSetToPermissionMutationConsumer> _logger;
        private readonly IResourceMutationService _resourceMutationService;

        public ResourceSetToPermissionMutationConsumer(
            ILogger<ResourceSetToPermissionMutationConsumer> logger,
            IResourceMutationService resourceMutationService)
        {
            _logger = logger;
            _resourceMutationService = resourceMutationService;
        }
        public async Task Consume(ResourceSetToPermissionMutationEvent context)
        {
            _logger.LogInformation("Start Consume for ProcessPermissionAsync");
            await _resourceMutationService.ProcessPermissionAsync(context);
        }
    }
}
