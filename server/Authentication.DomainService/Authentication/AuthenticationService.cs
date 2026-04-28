using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.Services;
using DomainService.Shared.RequestModel;
using DomainService.Utilities;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace DomainService.Authentication
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly ILogger<AuthenticationService> _logger;
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly ITenants _tenants;

        private const string Public_Cert_Cache_Prefix = "tetocertpublic::";

        public AuthenticationService(
            ILogger<AuthenticationService> logger,
            ICacheClient cacheClient,
            IAuthenticationRepository authenticationRepository,
            IAuthenticationDomainService authenticationDomainService,
            ITenants tenants
        )
        {
            _logger = logger;
            _cacheClient = cacheClient;
            _authenticationRepository = authenticationRepository;
            _authenticationDomainService = authenticationDomainService;
            _tenants = tenants;
        }

        public async Task<LogoutResponse> LogoutUser(string refreshToken, HttpRequest httpRequest)
        {
            _logger.LogInformation("Logout process start");

            var isAll = string.IsNullOrWhiteSpace(refreshToken);

            var result = isAll ? await ProcessLogoutAll() : await ProcessLogout(refreshToken);

            await ProcessTimeline(httpRequest, isAll);
            return new LogoutResponse
            {
                IsSuccess = result,
            };

        }

        public async Task<bool> ProcessLogout(string refreshToken)
        {
            await _cacheClient.RemoveKeyAsync(refreshToken);
            var bc = BlocksContext.GetContext();

            var result = await _authenticationRepository.UpdateSessionStatusAsync(refreshToken, bc?.UserId ?? "");

            return result;
        }

        public async Task<bool> ProcessLogoutAll()
        {
            var bc = BlocksContext.GetContext();

            var refreshTokens = (await _authenticationRepository.GetActiveSessionByUserIdAsync(bc.UserId)).Select(x => x.RefreshToken).ToList();
            var cacheTask = refreshTokens.Select(async x => await _cacheClient.RemoveKeyAsync(x));
            await Task.WhenAll(cacheTask);

            var result = await _authenticationRepository.UpdateSessionStatusForAllRefreshTokenAsync(refreshTokens);
            return result;
        }

        public async Task<bool> ProcessTimeline(HttpRequest httpRequest, bool isFromAll)
        {
            var bc = BlocksContext.GetContext();
            var eventTimeline = new UserAuthenticationTimelineEvent
            {
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(httpRequest?.Headers?.UserAgent),
                IpAddresses = string.Join(",", _authenticationDomainService.GetVisitorsIpAddresses(httpRequest.HttpContext)),
                Event = isFromAll ? "revoke_access_by_logout_all" : "revoke_access_by_logout",
                ActionBy = isFromAll ? "call_api_to_logout_all" : "call_api_to_logout",
                UserId = bc?.UserId?? ""
            };

            await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, eventTimeline);
            return true;
        }

        public async Task<OIDCClientCredential> GetClientCredentialAsync(string clientId)
        {
            return await _authenticationRepository.GetOIDCClientCredentialAsync(clientId);
        }

        public async Task<string> ConstructRedirectUriAsync(string clientId, AcknowledgeRequest request)
        {
            var client = await GetClientCredentialAsync(request.ClientId);
            var code = Guid.NewGuid().ToString("n");
            var stateInfo = new StateInfo { Scope = request.Scope, State = request.State, Code = code, Nonce = request.Nonce, UserName = request.Username, Audience = client.Audience ?? "", Provider = "SeliseCloud", NextUrl = client.RedirectUri };
            await _cacheClient.AddStringValueAsync(code, JsonSerializer.Serialize(stateInfo), 300);
            var uri = $"{client.RedirectUri}?code={code}";

            if (!string.IsNullOrEmpty(request.State))
                uri += $"&state={request.State}";

            return uri;
        }

        public string CookieToken(HttpRequest request)
        {
            var bc = BlocksContext.GetContext();  
            var refreshToken = request.HttpContext.Request.Cookies[$"{IdpConstants.RefreshTokenCookieName}_{bc.TenantId}"];
            refreshToken = string.IsNullOrEmpty(refreshToken) ? request.HttpContext.Request.Headers[$"{IdpConstants.RefreshTokenCookieName}_{bc.TenantId}"] : refreshToken;

            return refreshToken;
        }

        public bool DeleteCookie(HttpRequest request)
        {
            var cookieDomain = _tenants.GetTenantByID(BlocksContext.GetContext()?.TenantId ?? "")?.CookieDomain;
            var bc = BlocksContext.GetContext();    
            var cookieOptions = new CookieOptions
            {
                Domain = cookieDomain, 
                Path = "/",              
                Secure = true,
                HttpOnly = true,
                SameSite = SameSiteMode.None
            };

            request.HttpContext.Response.Cookies.Delete($"{IdpConstants.RefreshTokenCookieName}_{bc.TenantId}", cookieOptions);
            request.HttpContext.Response.Cookies.Delete($"{IdpConstants.AccessTokenCookieName}_{bc.TenantId}", cookieOptions);

            return true;
        }

        public async Task<IActionResult> GetLoginOptionsAsync()
        {
            var config = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            var ssoConfig = (await _authenticationDomainService.GetSocialLoginCredentialsAsync()).Where(c=>c.IsDisabled == false);

            return new OkObjectResult(new
            {
                AllowedGrantTypes = config.AllowedGrantTypes.Except(["mfa_code", "refresh_token"]),
                SsoInfo = config.AllowedGrantTypes.Contains("social")
                          ? ssoConfig.Select(info => new { info.Provider, info.Audience })
                          : null
            });
        }

        public async Task<ClaimsPrincipal?> GetPrincipalFromTokenAsync(HttpRequest request, string tenantId, bool IsUserInfoGetRequest = false)
        {
            var (token, _) =  TokenHelper.GetToken(request, _tenants);
            var tenant = _tenants.GetTenantByID(tenantId);

            if (!string.IsNullOrEmpty(token))
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                string cacheKey = $"{Public_Cert_Cache_Prefix}{tenant.TenantId}";
                var certificateData = await _cacheClient.CacheDatabase().StringGetAsync(cacheKey);
                var validationParams = tenant.JwtTokenParameters;
                var publicCert = X509CertificateLoader.LoadPkcs12(certificateData, validationParams.PublicCertificatePassword);
                var tokenValidationParameters = !IsUserInfoGetRequest? new TokenValidationParameters { ValidateLifetime = true, ClockSkew = TimeSpan.Zero, IssuerSigningKey = new X509SecurityKey(publicCert), ValidateIssuerSigningKey = true, ValidateIssuer = true, ValidIssuer = validationParams?.Issuer, ValidAudiences = validationParams?.Audiences, ValidateAudience = true, SaveSigninToken = true } :
                                                                      new TokenValidationParameters { ValidateLifetime = true, ClockSkew = TimeSpan.Zero, IssuerSigningKey = new X509SecurityKey(publicCert), ValidateIssuerSigningKey = true, ValidateIssuer = false, ValidateAudience = false, SaveSigninToken = true };
                return tokenHandler.ValidateToken(token, tokenValidationParameters, out _);
            }

            return null;
        }
    }
}
