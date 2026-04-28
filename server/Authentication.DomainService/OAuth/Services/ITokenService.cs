using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using Iam.DomainService.Entities;

namespace DomainService.OAuth
{
    public interface ITokenService
    {
        Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null);
    }
}
