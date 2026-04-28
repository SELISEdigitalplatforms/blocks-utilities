using Blocks.Genesis;
using DomainService.Projects;
using System;
using System.Collections.Generic;
using System.Text;

namespace Worker.Consumers.Identifier
{
    public class RestoreProjectConsumer : IConsumer<RestoreProjectRequest>
    {
        private readonly IProjectManagementService _projectManagementService;

        public RestoreProjectConsumer(IProjectManagementService projectManagementService)
        {
            _projectManagementService = projectManagementService;
        }

        public async Task Consume(RestoreProjectRequest context)
        {
            await _projectManagementService.RestoreUnfinishedProjectAsync();
        }
    }
}
