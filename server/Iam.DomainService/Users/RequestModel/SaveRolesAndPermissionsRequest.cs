using Blocks.Genesis;
using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Users
{
    public class SaveRolesAndPermissionsRequest : IProjectKey
    {
        public required string UserId { get; set; }
        public List<OrganizationMembership> Memberships { get; set; } = [];
        public string? ProjectKey { get; set; }

    }

}
