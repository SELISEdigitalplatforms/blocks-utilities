using Blocks.Genesis;

namespace DomainService.People
{
    public class ResendInvitationRequest
    {
        public string Email { get; set; }
        public string GroupId { get; set; }
    }

    public class ResendInvitationResponse : BaseResponse
    {
        public string? ItemId { get; set; }
    }
}
