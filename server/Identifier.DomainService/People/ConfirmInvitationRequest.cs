using Blocks.Genesis;

namespace DomainService.People
{
    public class ConfirmInvitationRequest
    {
        public string Code { get; set; }
    }

    public class ConfirmInvitationResponse : BaseMutationResponse
    {
        public string ActivationKey { get; set; }
    }
}
