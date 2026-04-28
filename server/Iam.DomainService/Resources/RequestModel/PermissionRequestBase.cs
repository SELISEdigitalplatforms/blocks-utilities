using Iam.DomainService.Enums;

namespace Iam.DomainService.Resources
{
    public class PermissionRequestBase
    {
        public string Name { get; set; }
        public ResourceType Type { get; set; }
        public string? Description { get; set; }
        public string Resource { get; set; }
        public string ResourceGroup { get; set; }
        public List<string> Tags { get; set; } = [];
        public List<string> DependentPermissions { get; set; } = [];
        public bool IsBuiltIn { get; set; }
        public PermissionSeverity PermissionSeverity { get; set; }
    }
}
