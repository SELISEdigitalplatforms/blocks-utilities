using Blocks.Genesis;
using DomainService.Dtos;

namespace DomainService.People
{
    public interface IPeopleService
    {
        Task<InviteResponse> InvitePeoplesAsync(InviteRequest requests);
        Task<BaseResponse> RemoveAccessFromProjectAsync(RemoveAccessRequest request);
        Task<GetPeoplesResponse> GetPeoplesAsync(GetPeoplesRequest request);
        Task<bool> SendProjectInvitationToNewUser(CreateUserByEmailPostEvent_Identifier @event);
        Task<ConfirmInvitationResponse> ConfirmInvitationAsync(ConfirmInvitationRequest request);
        Task<BaseResponse> ResendInvitationAsync(ResendInvitationRequest request);
        Task<SignupResponse> SignupAsync(SignupRequest request);

        Task<BaseResponse> TransferOwnershipAsync(TransferOwnershipRequest request);
    }
}
