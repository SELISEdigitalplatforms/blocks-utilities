using DomainService.OAuth.RequestModel;

namespace DomainService.OAuth
{
    public interface ISocialLogInServiceProvider
    {
        Task<GetSocialLogInEndPointResponse> GetSocialLogInEndPointAsync(GetSocialLogInEndPointRequest request);
        Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo);
    }
}
