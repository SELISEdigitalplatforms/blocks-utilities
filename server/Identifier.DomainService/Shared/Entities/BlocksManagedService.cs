using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Shared.Entities
{
    [BsonIgnoreExtraElements]
    public class BlocksManagedService : BaseEntity
    {
        public string Name { get; set; }
        public string TenantId { get; set; }
        public string Description { get; set; }
        public string ServiceId { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public string ServiceBusConnectionString { get; set; }
        public string ServiceType { get; set; }
    }

    public enum BlocksManagedServiceType
    {
        None = 0,
        Api = 1,
        Worker = 2,
    }
}
