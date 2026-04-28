using Iam.DomainService.Enums;

namespace Iam.DomainService.Dtos
{
    public class GetPermissionFilter
    {
        public string Search { get; set; }
        public ResourceType Type { get; set; }
        public PermissionSeverity PermissionSeverity { get; set; }
        public string IsBuiltIn { get; set; } // "yes"/"no"
        public List<string> Tags { get; set; } = [];
        public List<string> Resources { get; set; } = [];
        public bool IsArchived { get; set; }
        public string? ResourceGroup { get; set; }
    }
}
