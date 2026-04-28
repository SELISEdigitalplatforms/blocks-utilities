using Blocks.Genesis;
using System;
using System.Collections.Generic;
using System.Text;
using DomainService.Projects;
using DomainService.Shared;
namespace Worker.Consumers.Identifier
{
    public class ConfigureProjectConsumer : IConsumer<Tenant>
    {
        private readonly IProjectManagementService _projectManagementService;
        private readonly IDomainManagementService _domainManagementService;

        public ConfigureProjectConsumer(IProjectManagementService projectManagementService,
                                        IDomainManagementService domainManagementService)
        {
            _projectManagementService = projectManagementService;
            _domainManagementService = domainManagementService;
        }

        public async Task Consume(Tenant project)
        {
            if (project.CookieDomain != IdentifierConstants.BlocsDomain)
            {
                await Task.WhenAll(_domainManagementService.ConfigureDomainAsync(new ConfigureDomainRequest { CookieDomain = project.ApplicationDomain, ProjectKey = project.ItemId }),
                                   _projectManagementService.ConfigureProjectAsync(project));
            }
            else
            {
                await _projectManagementService.ConfigureProjectAsync(project);
            }
        }
    }
}
