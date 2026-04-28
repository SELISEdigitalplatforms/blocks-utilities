using Iam.DomainService.Enums;

namespace Iam.DomainService.Dtos
{
    public class ResourceSetToPermissionMutationEvent
    {
        public required List<string> AddPermissions { get; set; } = new List<string>();
        public List<string> RemovePermissions { get; set; } = new List<string>();
        public required string Slug { get; set; }
        public required ResourceEntity Entity { get; set; }
    }
}
