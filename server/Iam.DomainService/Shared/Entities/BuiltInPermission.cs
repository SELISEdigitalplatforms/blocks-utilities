using Blocks.Genesis;
using Iam.DomainService.Enums;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class BuiltInPermission : BaseEntity
    {
        public string Name { get; set; }
        public ResourceType Type { get; set; }
        public string Description { get; set; }
        public string Resource { get; set; }
        public string ResourceGroup { get; set; }
        public bool IsBuiltIn { get; set; }
        public bool IsArchived { get; set; }
        public PermissionSeverity PermissionSeverity { get; set; }
        public List<string> DependentPermissions { get; set; } = [];
    }


}
