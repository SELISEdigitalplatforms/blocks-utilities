using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace DomainService.OAuth
{
    public class OAuthJwtAccessTokenManager : IOAuthJwtAccessTokenManager
    {
        private readonly IJwtAccessTokenProvider _jwtAccessTokenProvider;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IOtpServiceFactory _otpServiceFactory;
        private readonly IMfaConfigurationService _configurationService;
        private readonly IConfiguration _configuration;
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;

        public OAuthJwtAccessTokenManager(
            IJwtAccessTokenProvider jwtAccessTokenProvider,
            IAuthenticationDomainService authenticationDomainService,
            IMfaConfigurationService configurationService,
            ICacheClient cacheClient,
            ITenants tenants,
            IOtpServiceFactory otpServiceFactory,
            IConfiguration configuration
        )
        {
            _jwtAccessTokenProvider = jwtAccessTokenProvider;
            _authenticationDomainService = authenticationDomainService;
            _configurationService = configurationService;
            _cacheClient = cacheClient;
            _tenants = tenants;
            _otpServiceFactory = otpServiceFactory;
            _configuration = configuration;
        }

        public async Task<TokenResponse> ManageTokenAsync(TokenRequest tokenRequest, AuthenticationConfiguration authenticationConfiguration, User user, StateInfo? stateInfo = null)
        {
            var bc = BlocksContext.GetContext();
            
            var tokenResponse = await ProcessCheckPoints(tokenRequest, user);

            if (tokenResponse != null && !string.IsNullOrWhiteSpace(tokenResponse.Error))
            {
                return tokenResponse;
            }

            var tenant = _tenants.GetTenantByID(bc?.TenantId ?? "");
            var jwtAccessToken = await _jwtAccessTokenProvider.GetJwtAccessToken(authenticationConfiguration, tenant, user, stateInfo, organizationId: tokenRequest.OrganizationId);
            jwtAccessToken.Audience = !string.IsNullOrWhiteSpace(stateInfo?.Audience) ? stateInfo.Audience : jwtAccessToken.Audience;
            jwtAccessToken.Issuer = tokenRequest.GrantType == GrantTypes.AuthCode ? _configuration["OpenIdConnect:IssuerUri"] ?? "Selise-Blocks": jwtAccessToken.Issuer;

            var accessToken = CreateJwtAccessToken(jwtAccessToken);
            var (refreshToken, refreshValidity) = await ManageRefreshTokenAsync(tokenRequest, jwtAccessToken, authenticationConfiguration, tenant, user);

            return new TokenResponse
            {
                AccessToken = accessToken,
                ExpiresIn = authenticationConfiguration.AccessTokenValidForNumberMinutes,
                ExpiresUtc = jwtAccessToken.Expires,
                RefreshToken = refreshToken,
                RefreshExpiresUtc = refreshValidity,
                CookieDomain = tenant.CookieDomain,
                StatusCode = 200
            };
        }

        private async Task<TokenResponse> ProcessCheckPoints(TokenRequest tokenRequest, User user)
        {
            if (tokenRequest.GrantType != GrantTypes.MfaCode && tokenRequest.GrantType != GrantTypes.ClientCredential && await CheckIfMfaIsApplicable(user))
            {
                return await HandleMfaAuthentication(user);
            }

            return new TokenResponse(); //Will send proper response after 20.04.2025

            // return ProcessAccountLock(tenant, user); 
        }

        private async Task<TokenResponse> HandleMfaAuthentication(User user)
        {
            var otpService = _otpServiceFactory.GetOTPService(user.UserMfaType);
            var response = await otpService.GenerateAsync(new UserInfo { Email = user.Email, ItemId = user.ItemId, Language = user.Language ?? "en-US" });

            return new TokenResponse
            {
                MfaId = response.MfaId,
                UserMfa = user.UserMfaType,
                Error = "mfa_enabled",
                ErrorDescription = "Mfa code required",
                StatusCode = 200
            };
        }


        private async Task<bool> CheckIfMfaIsApplicable(User user)
        {
            var mfaConfiguration = await _configurationService.GetAsync();
            var mfaProviders = mfaConfiguration.UserMfaType ?? [];

            return user.MfaEnabled && mfaProviders.Contains(user.UserMfaType);
        }

        public static string CreateJwtAccessToken(JwtAccessToken jwtAccessToken, StateInfo? stateInfo = null)
        {
            

            var jwtToken = new JwtSecurityToken(
                jwtAccessToken.Issuer,
                jwtAccessToken.Audience,
                jwtAccessToken.Claims,
                jwtAccessToken.NotBefore,
                jwtAccessToken.Expires,
                jwtAccessToken.SigningCredentials);

            return new JwtSecurityTokenHandler().WriteToken(jwtToken);
        }

        public async Task<(string, DateTime)> ManageRefreshTokenAsync(TokenRequest tokenRequest, JwtAccessToken jwtAccessToken, AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user)
        {
            var visitorsIpAddresses = _authenticationDomainService.GetVisitorsIpAddresses(tokenRequest.Request.HttpContext) ?? new List<string>();

            // Check if this is a refresh token grant type
            if (tokenRequest.GrantType == GrantTypes.RefreshToken || tokenRequest.GrantType == GrantTypes.SwitchOrganization)
            {
                return await HandleRefreshTokenGrant(tokenRequest, tenant, user, visitorsIpAddresses);
            }
            else
            {
                // Initial auth flow - create new refresh token with full configured lifetime
                return await CreateNewRefreshToken(tokenRequest, tenant, user, authenticationConfiguration, visitorsIpAddresses);
            }
        }

        private async Task<(string, DateTime)> HandleRefreshTokenGrant(TokenRequest tokenRequest, Tenant tenant, User user, IEnumerable<string> visitorsIpAddresses)
        {
            // Validate refresh token exists
            if (string.IsNullOrWhiteSpace(tokenRequest.RefreshToken))
            {
                return (string.Empty, DateTime.MinValue);
            }

            // Case 1: Check if refresh token exists in Redis
            var oldRefreshTokenCache = await _cacheClient.GetStringValueAsync(tokenRequest.RefreshToken);
            
            if (string.IsNullOrEmpty(oldRefreshTokenCache))
            {
                // Case 2: Token doesn't exist - return empty to signal error
                return (string.Empty, DateTime.MinValue);
            }

            var oldRefreshToken = JsonSerializer.Deserialize<RefreshTokenCache>(oldRefreshTokenCache);
            if (oldRefreshToken == null)
            {
                // Case 2: Invalid token data
                return (string.Empty, DateTime.MinValue);
            }

            // Calculate remaining TTL
            var remainingMinutes = (int)(oldRefreshToken.ExpiresUtc - DateTime.UtcNow).TotalMinutes;

            // Case 3: Token exists but TTL is too low (less than 1 minute)
            if (remainingMinutes < 1)
            {
                // Delete expired token and send revocation event
                await _cacheClient.RemoveKeyAsync(tokenRequest.RefreshToken);
                
                var revokeEvent = new RefreshTokenEvent
                {
                    RefreshToken = tokenRequest.RefreshToken ?? string.Empty,
                    TenantId = oldRefreshToken.TenantId,
                    IssuedUtc = oldRefreshToken.IssuedUtc,
                    ExpiresUtc = oldRefreshToken.ExpiresUtc,
                    IpAddresses = oldRefreshToken.IpAddresses ?? string.Empty,
                    UserId = oldRefreshToken.UserId ?? string.Empty,
                    DeviceInformation = _authenticationDomainService.GetDeviceInfo(tokenRequest.Request?.Headers?.UserAgent),
                    IsRevoke = true,
                    IsLogin = false,
                    GrantType = tokenRequest.GrantType
                };
                await _authenticationDomainService.SendToQueueAsync(Utilities.IdpConstants.AuthenticationQueue, revokeEvent);
                
                return (string.Empty, DateTime.MinValue);
            }

            // Case 1: Token exists and has sufficient TTL - rotate token
            var newRefreshTokenId = Guid.NewGuid().ToString("N");
            var newRefreshTokenExpireOn = DateTime.UtcNow.AddMinutes(remainingMinutes);

            var newRefreshTokenCache = new RefreshTokenCache
            {
                RefreshToken = newRefreshTokenId,
                TenantId = oldRefreshToken.TenantId,
                IssuedUtc = DateTime.UtcNow,
                ExpiresUtc = newRefreshTokenExpireOn,
                IpAddresses = string.Join(",", visitorsIpAddresses),
                UserId = oldRefreshToken.UserId ?? string.Empty
            };

            // Save new token to Redis with remaining TTL
            await _cacheClient.AddStringValueAsync(newRefreshTokenId, JsonSerializer.Serialize(newRefreshTokenCache), remainingMinutes * 60);

            // Delete old token from Redis
            await _cacheClient.RemoveKeyAsync(tokenRequest.RefreshToken);

            // Send revocation event for old token
            var revokeOldTokenEvent = new RefreshTokenEvent
            {
                RefreshToken = tokenRequest.RefreshToken ?? string.Empty,
                TenantId = oldRefreshToken.TenantId,
                IssuedUtc = oldRefreshToken.IssuedUtc,
                ExpiresUtc = oldRefreshToken.ExpiresUtc,
                IpAddresses = oldRefreshToken.IpAddresses ?? string.Empty,
                UserId = oldRefreshToken.UserId ?? string.Empty,
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(tokenRequest.Request?.Headers?.UserAgent),
                IsRevoke = true,
                IsLogin = false,
                GrantType = tokenRequest.GrantType
            };
            await _authenticationDomainService.SendToQueueAsync(Utilities.IdpConstants.AuthenticationQueue, revokeOldTokenEvent);

            // Send creation event for new token (renewal, not login)
            var addNewTokenEvent = new RefreshTokenEvent
            {
                RefreshToken = newRefreshTokenCache.RefreshToken,
                TenantId = newRefreshTokenCache.TenantId,
                IssuedUtc = newRefreshTokenCache.IssuedUtc,
                ExpiresUtc = newRefreshTokenCache.ExpiresUtc,
                IpAddresses = newRefreshTokenCache.IpAddresses,
                UserId = newRefreshTokenCache.UserId,
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(tokenRequest.Request?.Headers?.UserAgent),
                IsRevoke = false,
                IsLogin = false,
                GrantType = tokenRequest.GrantType
            };
            await _authenticationDomainService.SendToQueueAsync(Utilities.IdpConstants.AuthenticationQueue, addNewTokenEvent);

            return (newRefreshTokenId, newRefreshTokenExpireOn);
        }

        private async Task<(string, DateTime)> CreateNewRefreshToken(TokenRequest tokenRequest, Tenant tenant, User user, AuthenticationConfiguration authenticationConfiguration, IEnumerable<string> visitorsIpAddresses)
        {
            var refreshTokenId = Guid.NewGuid().ToString("N");

            // Initial auth flow - use full configured lifetime
            var configuredRefreshTokenLifetime = authenticationConfiguration.RefreshTokenValidForNumberMinutes > 0
                ? authenticationConfiguration.RefreshTokenValidForNumberMinutes
                : 15;

            var configuredRememberMeLifetime = authenticationConfiguration.RememberMeRefreshTokenValidForNumberMinutes > 0
                ? authenticationConfiguration.RememberMeRefreshTokenValidForNumberMinutes
                : configuredRefreshTokenLifetime;

            var refreshTokenLifetime = tokenRequest.RememberMe
                ? configuredRememberMeLifetime
                : configuredRefreshTokenLifetime;

            var refreshTokenExpireOn = DateTime.UtcNow.AddMinutes(refreshTokenLifetime);

            var refreshTokenCache = new RefreshTokenCache
            {
                RefreshToken = refreshTokenId,
                TenantId = tenant.TenantId,
                IssuedUtc = DateTime.UtcNow,
                ExpiresUtc = refreshTokenExpireOn,
                IpAddresses = string.Join(",", visitorsIpAddresses),
                UserId = user.ItemId ?? string.Empty
            };

            await _cacheClient.AddStringValueAsync(refreshTokenCache.RefreshToken, JsonSerializer.Serialize(refreshTokenCache), refreshTokenLifetime * 60);

            var addRefreshTokenCommand = new RefreshTokenEvent
            {
                RefreshToken = refreshTokenCache.RefreshToken,
                TenantId = refreshTokenCache.TenantId,
                IssuedUtc = refreshTokenCache.IssuedUtc,
                ExpiresUtc = refreshTokenCache.ExpiresUtc,
                IpAddresses = refreshTokenCache.IpAddresses,
                UserId = refreshTokenCache.UserId,
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(tokenRequest.Request?.Headers?.UserAgent),
                IsRevoke = false,
                IsLogin = true,
                GrantType = tokenRequest.GrantType
            };
            
            await _authenticationDomainService.SendToQueueAsync(Utilities.IdpConstants.AuthenticationQueue, addRefreshTokenCommand);

            return (refreshTokenId, refreshTokenExpireOn);
        }

        public TokenResponse ProcessAccountLock(AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user)
        {
            var lockKey = $"account-lock-{tenant.TenantId}-{user.ItemId}-{user.OrganizationIds?.FirstOrDefault() ?? "default"}";
            var isLocked = IsLocked(lockKey, authenticationConfiguration.GetNumberOfWrongAttemptsToLockTheAccount);

            if (!isLocked)
            {
                Lock(lockKey, authenticationConfiguration.AccountLockDurationInMinutes, authenticationConfiguration.GetNumberOfWrongAttemptsToLockTheAccount);
                return new TokenResponse();
            }

            return new TokenResponse { Error = OAuthError.AccountLocked, ErrorDescription = "Your account has been locked due to multiple failed login attempts" };
        }

        public void Lock(string key, int lockTimeInMinutes, int maxAttempts)
        {
            var lockCountValue = _cacheClient.GetStringValue(key);
            var lockCount = string.IsNullOrWhiteSpace(lockCountValue) ? 0 : int.Parse(lockCountValue);

            if (lockCount >= maxAttempts)
            {
                return;
            }

            _cacheClient.AddStringValue(key, (lockCount + 1).ToString(), lockTimeInMinutes * 60);
        }

        public bool IsLocked(string key, int maxAttempts)
        {
            var lockCountValue = _cacheClient.GetStringValue(key);

            return !string.IsNullOrWhiteSpace(lockCountValue) && int.Parse(lockCountValue) >= maxAttempts;
        }
    }
}