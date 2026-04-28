using Blocks.Genesis;
using DomainService.Entities;
using Iam.DomainService.Entities;

namespace DomainService.OAuth
{
    public interface IJwtAccessTokenProvider
    {
        Task<JwtAccessToken> GetJwtAccessToken(AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user, StateInfo? state = null, string? organizationId = null);
    }
}
