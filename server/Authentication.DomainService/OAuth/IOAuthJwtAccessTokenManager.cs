using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using Iam.DomainService.Entities;

namespace DomainService.OAuth
{
    public interface IOAuthJwtAccessTokenManager
    {
        Task<TokenResponse> ManageTokenAsync(TokenRequest tokenRequest, AuthenticationConfiguration authenticationConfiguration, User user, StateInfo? stateInfo = null);
        Task<(string, DateTime)> ManageRefreshTokenAsync(TokenRequest tokenRequest, JwtAccessToken jwtAccessToken, AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user);
    }
}