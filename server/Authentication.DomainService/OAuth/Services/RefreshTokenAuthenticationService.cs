using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;

namespace DomainService.OAuth
{
    public class RefreshTokenAuthenticationService : ITokenService
    {
        private readonly ILogger<RefreshTokenAuthenticationService> _logger;
        private readonly IJwtAccessTokenProvider _jwtAccessTokenProvider;
        private readonly ITenants _tenants;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        
        public RefreshTokenAuthenticationService(
            ILogger<RefreshTokenAuthenticationService> logger,
            IJwtAccessTokenProvider jwtAccessTokenProvider,
            ITenants tenants,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager
        )
        {
            _logger = logger;
            _jwtAccessTokenProvider = jwtAccessTokenProvider;
            _tenants = tenants;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
        }
        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User user)
        {
            _logger.LogInformation("Authenticate start for RefreshToken");
            var bc = BlocksContext.GetContext();
            var tenant = _tenants.GetTenantByID(bc?.TenantId);
            var jwtAccessToken = await _jwtAccessTokenProvider.GetJwtAccessToken(authenticationConfiguration, tenant, user, organizationId: request.OrganizationId);
            var jwtToken = new JwtSecurityToken(
                jwtAccessToken.Issuer,
                jwtAccessToken.Audience,
                jwtAccessToken.Claims,
                jwtAccessToken.NotBefore,
                jwtAccessToken.Expires,
                jwtAccessToken.SigningCredentials);

            var accessToken = new JwtSecurityTokenHandler().WriteToken(jwtToken);

            // Generate new refresh token every time refresh token is called
            var refreshTokenResult = await _oAuthJwtAccessTokenManager.ManageRefreshTokenAsync(request, jwtAccessToken, authenticationConfiguration, tenant, user);
            var newRefreshToken = refreshTokenResult.Item1;
            var refreshTokenExpiry = refreshTokenResult.Item2;

            // Check if refresh token generation failed (returns empty string for error cases)
            if (string.IsNullOrEmpty(newRefreshToken))
            {
                return new TokenResponse
                {
                    Error = OAuthError.InvalidRefreshToken,
                    ErrorDescription = "Refresh token is invalid or expired",
                    StatusCode = 400
                };
            }

            return new TokenResponse
            {
                AccessToken = accessToken,
                ExpiresIn = authenticationConfiguration.AccessTokenValidForNumberMinutes,
                ExpiresUtc = jwtAccessToken.Expires,
                RefreshToken = newRefreshToken,
                RefreshExpiresUtc = refreshTokenExpiry,
                CookieDomain = tenant.CookieDomain,
            };
        }
    }
}