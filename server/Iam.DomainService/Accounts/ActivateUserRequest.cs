using Blocks.Genesis;

namespace Iam.DomainService.Accounts
{
    public class ActivateUserRequest : BaseAccountRequest, IProjectKey
    {
        public string? MailPurpose { get; set; }
        public bool PreventPostEvent { get; set; }
        public string? ProjectKey { get; set; }

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

}
