using Blocks.Genesis;
using DomainService.Shared.Entities;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.ManagedService
{
    [BsonIgnoreExtraElements]
    public class RegisterServiceRequest : IProjectKey
    {
        public string ServiceName { get; set; }
        public string? Description { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = [];
        public List<string> Tags { get; set; } = [];
        public string ProjectKey { get ; set ; }
        public string ServiceType { get; set; }
    }
}
