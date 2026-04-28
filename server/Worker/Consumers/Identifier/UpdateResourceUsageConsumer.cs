using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Shared.Dtos;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Text;

namespace Worker.Consumers.Identifier
{
    public class UpdateResourceUsageConsumer : IConsumer<UpdateResourceUsageCommand_Identifier>
    {
        private readonly IDbContextProvider _dbContextProvider;
        public UpdateResourceUsageConsumer(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task Consume(UpdateResourceUsageCommand_Identifier context)
        {
            var collection = _dbContextProvider.GetDatabase(context.TenantId).GetCollection<ResourceLimit>("ResourceLimits");
            var filter = Builders<ResourceLimit>.Filter.Eq(r => r.Resource, context.Resource);
            var update = Builders<ResourceLimit>.Update.Inc(r => r.Usage, context.Amount);
            await collection.UpdateOneAsync(filter, update);
        }
    }
}
