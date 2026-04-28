namespace Iam.DomainService.Shared.Entities
{
    public class OrganizationMembership
    {
        public string OrganizationId { get; set; }
        public List<string> Roles { get; set; } = [];
        public List<string> Permissions { get; set; } = [];
    }
}
