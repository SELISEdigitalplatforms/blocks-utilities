using Blocks.Genesis;

namespace DomainService.People
{
    public class InviteRequest
    {
       public Dictionary<string, List<string>> Invitations { get; set; } = new Dictionary<string, List<string>>();
       public required string GroupId { get; set; }
    }

    public class InvitationDetails
    {
        public List<string> Emails { get; set; } = [];
        public string ProjectKey { get; set; }
    }

    public class InviteResponse : BaseResponse
    {
        public string? ItemId { get; set; }
    }
}
