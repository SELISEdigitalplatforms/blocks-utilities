using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Resources;

namespace Worker.Consumers
{
    public class ResourceMutationConsumer : IConsumer<ResourceMutationEvent>
    {
        private readonly ILogger<ResourceMutationConsumer> _logger;
        private readonly IResourceMutationService _resourceMutationService;

        public ResourceMutationConsumer(ILogger<ResourceMutationConsumer> logger, IResourceMutationService resourceMutationService)
        {
            _logger = logger;
            _resourceMutationService = resourceMutationService;
        }

        public async Task Consume(ResourceMutationEvent context)
        {
            _logger.LogInformation("Start Consume for ExecuteResourceMutationCommandAsync");
            await _resourceMutationService.ExecuteResourceMutationCommandAsync(context);
        }
    }
}
