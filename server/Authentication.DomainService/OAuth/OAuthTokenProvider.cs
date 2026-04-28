using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.OAuth.Services;
using DomainService.Services;
using DomainService.Utilities;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DomainService.OAuth
{
    public class OAuthTokenProvider : IOAuthTokenProvider
    {
        private readonly IDictionary<string, ITokenService> _authenticationServices;
        private readonly ILogger<OAuthTokenProvider> _logger;
        private readonly IAuthenticationRepository _oAuthRepository;
        private readonly IConfiguration _configuration;
        private readonly ICacheClient _cacheClient;
        private readonly ISocialLogInServiceProvider _socialLogInServiceProvider;
        private readonly ITenants _tenants;

        public OAuthTokenProvider(
            ILogger<OAuthTokenProvider> logger,
            IServiceProvider serviceProvider,
            IAuthenticationRepository oAuthRepository,
            IConfiguration configuration,
            ICacheClient cacheClient,
            ISocialLogInServiceProvider socialLogInServiceProvider,
            ITenants tenants
        )
        {
            _authenticationServices = new SortedDictionary<string, ITokenService>
            {
                { GrantTypes.RefreshToken, serviceProvider.GetService<RefreshTokenAuthenticationService>() },
                { GrantTypes.SwitchOrganization, serviceProvider.GetService<RefreshTokenAuthenticationService>() },
                { GrantTypes.Password, serviceProvider.GetService<PasswordAuthenticationService>() },
                { GrantTypes.MfaCode, serviceProvider.GetService<MfaAuthorizationService>() },
                { GrantTypes.Social, serviceProvider.GetService<SocialAuthorizationService>() },
                { GrantTypes.AuthCode, serviceProvider.GetService<AuthorizeCodeService>() },
                { GrantTypes.BiometricAuthorization, serviceProvider.GetService<BiometricAuthorizationService>() },
                { GrantTypes.ClientCredential, serviceProvider.GetService<ClientCredentialAuthorizationService>() },
                { GrantTypes.ClientUserCode, serviceProvider.GetService<ClientUserCodeAuthorizationService>() },
                { GrantTypes.SsoConsentCode, serviceProvider.GetService<SSOConsentAuthenticationService>() }
            };

            _logger = logger;
            _oAuthRepository = oAuthRepository;
            _configuration = configuration;
            _cacheClient = cacheClient;
            _socialLogInServiceProvider = socialLogInServiceProvider;
            _tenants = tenants;
        }

        public async Task<IActionResult> AuthenticateAsync(TokenRequest request)
        {
            var bc = BlocksContext.GetContext();
            var config = await _oAuthRepository.GetAuthenticationConfigurationAsync();

            if (config == null)
            {
                _logger.LogWarning("Authentication configuration not found for tenant {TenantId}", bc?.TenantId);
                return OAuthError.InvalidRequest("Authentication configuration not found");
            }

            if (request.GrantType != "sso_consent" && !config.AllowedGrantTypes.Any(x => x == request.GrantType))
            {
                _logger.LogWarning("Grant type not allowed for tenant {TenantId}", bc?.TenantId);
                return OAuthError.InvalidRequest("Grant type not allowed for this tenant");
            }

            if (!_authenticationServices.TryGetValue(request.GrantType, out var authenticationService) || authenticationService == null)
            {
                return OAuthError.UnsupportedGrantType(request.GrantType);
            }

            return await HandleAuthenticationAsync(authenticationService, request, config);
        }

        public async Task<IActionResult> HandleAuthenticationAsync(ITokenService authService, TokenRequest request, AuthenticationConfiguration authenticationConfiguration)
        {
            _logger.LogInformation("Token creation start -- grant_type -- {Gt}", request.GrantType);
            

            var response = (request.GrantType == GrantTypes.RefreshToken || request.GrantType == GrantTypes.SwitchOrganization)
                         ? await GetTokenResponseForRefreshToken(authService, request, authenticationConfiguration)
                         : await GetTokenResponse(authService, request, authenticationConfiguration);

            _logger.LogInformation("Token creation end. Sending the response");
            var bc = BlocksContext.GetContext();
            if (string.IsNullOrWhiteSpace(response.Error))
            {
                if (!string.IsNullOrWhiteSpace(response.SsoUserRedirectUrl))
                {
                    return OAuthResponse.SSOUserNotExistResponse(response);
                }

                if (!string.IsNullOrWhiteSpace(response.CookieDomain))
                {
                    var accessTokenkey = $"{IdpConstants.AccessTokenCookieName}_{bc.TenantId}";
                    var refreshTokenKey = $"{IdpConstants.RefreshTokenCookieName}_{bc.TenantId}";
                    request.Request.HttpContext.Response.Cookies.Delete(accessTokenkey);
                    var cookieOptionsForDomain = GetCookieOptions("blocksdevelopers.com", response.ExpiresUtc);

                    request.Request.HttpContext.Response.Cookies.Append(
                        accessTokenkey,
                        response.AccessToken,
                        cookieOptionsForDomain
                    );

                    if (!string.IsNullOrWhiteSpace(response.RefreshToken))
                    {
                        cookieOptionsForDomain.Expires = response.RefreshExpiresUtc;
                        request.Request.HttpContext.Response.Cookies.Delete(refreshTokenKey);
                        request.Request.HttpContext.Response.Cookies.Append(
                            refreshTokenKey,
                            response.RefreshToken,
                            cookieOptionsForDomain
                        );
                    }
                }

                return OAuthResponse.TokenResponse(response, request);
            }

            return response.Error switch
            {
                OAuthError.MfaEnabled => OAuthResponse.MfaResponse(response),
                OAuthError.CaptchaEnabled => OAuthResponse.CaptchaResponse(),
                _ => response.StatusCode == 401 ? OAuthError.Error401Response(response.Error, response.ErrorDescription)
                    : OAuthError.Error400Response(response.Error, response.ErrorDescription)
            };
        }


        public CookieOptions GetCookieOptions(string domain, DateTime expires)
        {
            return new CookieOptions
            {
                Domain = domain,
                Expires = expires,
                HttpOnly = true,
                Path = "/",
                Secure = true,
                SameSite = SameSiteMode.None
            };
        }

        public async Task<TokenResponse> GetTokenResponse(ITokenService authService, TokenRequest request, AuthenticationConfiguration authenticationConfiguration)
        {
            if (!IsValidRequest(request)) return OAuthError.InValidResponse(request);

            return await authService.AuthenticateAsync(request, authenticationConfiguration);
        }

        private bool IsValidRequest(TokenRequest request) =>

              request.GrantType switch
              {
                  GrantTypes.Password => !string.IsNullOrWhiteSpace(request.Username),

                  GrantTypes.MfaCode => !string.IsNullOrWhiteSpace(request.Code) &&
                                        !string.IsNullOrWhiteSpace(request.MfaId) &&
                                         request.MfaType != UserMfaType.None,

                  GrantTypes.Social => !string.IsNullOrWhiteSpace(request.Code) &&
                                       !string.IsNullOrWhiteSpace(request.State),

                  GrantTypes.AuthCode => !string.IsNullOrWhiteSpace(request.Code),

                  GrantTypes.BiometricAuthorization => !string.IsNullOrWhiteSpace(request.BiometricId) &&
                                                       !string.IsNullOrWhiteSpace(request.BiometricKey),

                  GrantTypes.ClientCredential => !string.IsNullOrWhiteSpace(request.ClientId) &&
                                                 !string.IsNullOrWhiteSpace(request.ClientSecret),

                  GrantTypes.ClientUserCode => !string.IsNullOrWhiteSpace(request.ClientId) &&
                                               !string.IsNullOrWhiteSpace(request.UserCode),

                  GrantTypes.SwitchOrganization => !string.IsNullOrWhiteSpace(request.OrganizationId) && 
                                                   !string.IsNullOrWhiteSpace(request.RefreshToken),

                  GrantTypes.SsoConsentCode => !string.IsNullOrWhiteSpace(request.Code),

                  _ => false
              };

        public async Task<TokenResponse> GetTokenResponseForRefreshToken(ITokenService authService, TokenRequest request, AuthenticationConfiguration authenticationConfiguration)
        {
            var bc = BlocksContext.GetContext();   
            var cookieToken = request.Request.HttpContext.Request.Cookies[$"{IdpConstants.RefreshTokenCookieName}_{bc.TenantId}"];
            cookieToken = string.IsNullOrEmpty(cookieToken) ? request.Request.HttpContext.Request.Headers[$"{IdpConstants.RefreshTokenCookieName}_{bc.TenantId}"] : cookieToken;
            cookieToken = string.IsNullOrEmpty(cookieToken) ? request.RefreshToken : cookieToken;

            if (string.IsNullOrEmpty(cookieToken))
            {
                return new TokenResponse
                {
                    Error = OAuthError.RefreshTokenCookieNotFound
                };
            }

            // Ensure request.RefreshToken always reflects the resolved token (cookie/header/body)
            request.RefreshToken = cookieToken;

            var refreshTokenCache = await _cacheClient.GetStringValueAsync(cookieToken);

            if (string.IsNullOrEmpty(refreshTokenCache))
            {
                return new TokenResponse
                {
                    Error = OAuthError.InvalidRefreshToken
                };
            }

            var refreshToken = JsonSerializer.Deserialize<RefreshTokenCache>(refreshTokenCache);

            if (refreshToken == null || string.IsNullOrEmpty(refreshToken.RefreshToken) || string.IsNullOrEmpty(refreshToken.UserId))
            {
                return new TokenResponse
                {
                    Error = OAuthError.InvalidRefreshToken
                };
            }

            var user = await _oAuthRepository.GetUserByIdAsync(refreshToken.UserId);

            if (user == null)
            {
                return new TokenResponse
                {
                    Error = OAuthError.InvalidRefreshToken
                };
            }

            if (!user.Active || !user.IsVarified) 
                return OAuthError.UserNotActiveOrVerifiedResponse();

            if (!string.IsNullOrWhiteSpace(request.OrganizationId) && !user.Memberships.Any(org=>org.OrganizationId == request.OrganizationId))
                return OAuthError.InValidOrganization(request.OrganizationId);

            return await authService.AuthenticateAsync(request, authenticationConfiguration, user);

        }

        public async Task<GetSocialLogInEndPointResponse> GetSocialLogInEndPointAsync(GetSocialLogInEndPointRequest request)
        {
            return await _socialLogInServiceProvider.GetSocialLogInEndPointAsync(request);
        }
    }
}