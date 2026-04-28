using DomainService.OAuth.RequestModel;

namespace DomainService.OAuth
{
    internal interface ISocialLogInService
    {
        Task<(string, bool)> GetProviderLogInUriAsync(GetSocialLogInEndPointRequest loginData);
        Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo);
    }
}
