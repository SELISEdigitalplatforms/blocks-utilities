namespace Iam.DomainService.Dtos
{
    public class GetUsersFilter
    {
        public string Email { get; set; }
        public string Name { get; set; }
        public List<string> UserIds { get; set; } = [];
        public Status? Status { get; set; }
        public MFA? Mfa { get; set; }
        public DateTime? JoinedOn { get; set; }
        public DateTime? LastLogin { get; set; }
        public string? OrganizationId { get; set; } = null;
    }

    public class Status
    {
        public bool Active { get; set; }
        public bool Inactive { get; set; }
    }

    public class MFA
    {
        public bool Enabled { get; set; }
        public bool Disabled { get; set; }
    }
}
