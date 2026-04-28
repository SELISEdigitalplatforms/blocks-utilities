using Blocks.Genesis;
using DomainService.Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Worker.Consumers.Identifier
{
    public class DomainConfigureConsumer : IConsumer<ConfigureDomainRequest>
    {
        private readonly IDomainManagementService _domainManagementService;

        public DomainConfigureConsumer(IDomainManagementService domainManagementService)
        {
            _domainManagementService = domainManagementService;
        }

        public async Task Consume(ConfigureDomainRequest context)
        {
            await _domainManagementService.ConfigureDomainAsync(context);
        }
    }
}
