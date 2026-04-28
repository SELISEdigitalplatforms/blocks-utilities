using Blocks.Genesis;
using CloudConfiguration.DomainService.Authentication;
using CloudConfiguration.DomainService.Authentication.RequestModel;
using CloudConfiguration.DomainService.Shared.Services;
using DomainService.Authentication;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.RequestModel;
using DomainService.ResponseModel;
using DomainService.Services;
using DomainService.Shared;
using DomainService.Shared.RequestModel;
using DomainService.Shared.ResponseModel;
using DomainService.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class AuthenticationController : ControllerBase
    {
        private readonly IOAuthTokenProvider _oAuthTokenProvider;
        private readonly IAuthenticationService _authenticationService;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IConfiguration _configuration;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ChangeControllerContext _changeControllerContext;
        public readonly IConfigurationService _confirurationService;
        public AuthenticationController(IOAuthTokenProvider oAuthTokenProvider,
                              IAuthenticationService authenticationService,
                              IConfiguration configuration,
                              IAuthenticationDomainService authenticationDomainService,
                              IAuthenticationRepository authenticationRepository,
                              ChangeControllerContext changeControllerContext, IConfigurationService confirurationService)
        {
            _oAuthTokenProvider = oAuthTokenProvider;
            _authenticationService = authenticationService;
            _configuration = configuration;
            _changeControllerContext = changeControllerContext;
            _authenticationDomainService = authenticationDomainService;
            _authenticationDomainService = authenticationDomainService;
            _authenticationRepository = authenticationRepository;
            _confirurationService = confirurationService;
        }

        #region Authorization Endpoints

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
        {
            var refreshToken = request != null && !string.IsNullOrWhiteSpace(request.RefreshToken) ? request.RefreshToken : _authenticationService.CookieToken(Request);

            var result = string.IsNullOrWhiteSpace(refreshToken) ? new LogoutResponse() : await _authenticationService.LogoutUser(refreshToken, Request);

            if (result.IsSuccess)
            {
                _authenticationService.DeleteCookie(Request);
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> LogoutAll()
        {
            var result = await _authenticationService.LogoutUser(string.Empty, Request);

            if (result.IsSuccess)
            {
                _authenticationService.DeleteCookie(Request);
                return Ok(result);
            }
            return BadRequest(result);
        }

        #endregion

        #region Client

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<SaveOIDCClientResponse> SaveOIDCClient([FromBody] SaveOIDCClientRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _authenticationDomainService.SaveOIDCClientAsync(request);
        }

        [ProtectedEndPoint]
        [HttpGet]
        public async Task<GetOIDCClientResponse> GetOIDCClient([FromQuery] GetOIDCClientRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _authenticationDomainService.GetOIDCClientAsyncAsync(request.ClientId);
        }

        [ProtectedEndPoint]
        [HttpGet]
        public async Task<GetOIDCClientsResponse> GetOIDCClients([FromQuery] GetOIDCClientsRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _authenticationDomainService.GetOIDCClientsAsyncAsync();
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<BaseResponse> DeleteOIDCClient([FromBody] DeleteOIDCClientRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _authenticationDomainService.DeleteOIDCClientAsyncAsync(request);
        }

        [Authorize]
        [HttpPost]
        public async Task<BaseResponse> GenerateUserCode([FromBody] GenerateUserCodeRequest request)
        {
            return await _authenticationDomainService.GenerateUserCodeByClientAsync(request);
        }

        [Authorize]
        [HttpGet]
        public async Task<List<GetUserCodesByUserIdResponse>> GetUserCodes()
        {
            return await _authenticationRepository.GetUserCodesByUserIdAsync(BlocksContext.GetContext()?.UserId);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<BaseResponse> SaveClientCredential([FromBody] SaveClientCredentialRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _authenticationDomainService.SaveClientCredentialAsync(request);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<BaseResponse> DeleteClientCredential([FromBody] DeleteClientCredentialRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _authenticationDomainService.DeleteClientCredentialAsync(request);
        }

        [ProtectedEndPoint]
        [HttpGet]
        public async Task<List<ClientCredential>> GetClientCredentials([FromQuery] GetAllClientCredentialsRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _authenticationRepository.GetClientCredentialsAsync();
        }

        #endregion

        #region OAuth

        [HttpGet]
        public async Task<IActionResult> Authorize([FromQuery] AuthorizeRequest request)
        {
            if (request.ResponseType == "code")
            {
                var client = await _authenticationService.GetClientCredentialAsync(request.ClientId);

                if (client == null || client.RedirectUri != request.RedirectUri || client.Scope != request.Scope)
                {
                    var errorPageUri = _configuration["OpenIdConnect:ErrorPageRedirectonUri"];
                    return Redirect(Helper.GetAuthorizationError(errorPageUri, request, client));
                }

                var claimsPrincipal = await _authenticationService.GetPrincipalFromTokenAsync(Request, BlocksContext.GetContext().TenantId);
                string redirectUri = $" {_configuration["OpenIdConnect:RedirectUri"]}?x-blocks-key={BlocksContext.GetContext()?.TenantId}&clientId={client.ItemId}";

                if (claimsPrincipal is not null)
                {
                    var userName = User.FindFirst(ClaimTypes.Email)?.Value;
                    redirectUri = $"{redirectUri}&userName={userName}";
                }

                if (!string.IsNullOrWhiteSpace(client.ClientLogoUrl) && !string.IsNullOrWhiteSpace(client.ClientBrandColor))
                {
                    redirectUri = $"{redirectUri}&brandColor={client.ClientBrandColor}&logoUrl={client.ClientLogoUrl}";
                }

                if(!string.IsNullOrWhiteSpace(request.State))
                {
                    redirectUri = $"{redirectUri}&state={request.State}&redirect_uri={client.RedirectUri}&scope={client.Scope}";
                }

                return Redirect(redirectUri);
            }

            return OAuthError.InvalidRequest("Unsupported response_type");
        }

        [HttpPost]
        public async Task<IActionResult> Token([FromForm] TokenPayload request)
        {
            return await _oAuthTokenProvider.AuthenticateAsync(new TokenRequest
            {
                GrantType = request.GrantType,
                Code = request.Code,
                MfaId = request.MfaId,
                MfaType = request.MfaType,
                RedirectUri = request.RedirectUri,
                Username = request.Username,
                Password = request.Password,
                Scope = request.Scope,
                RememberMe = request.RememberMe,
                Request = Request,
                RefreshToken = request.RefreshToken,
                State = request.State,
                Language = request.Language,
                BiometricId = request.BiometricId,
                BiometricKey = request.BiometriKey,
                ClientId = request.ClientId,
                ClientSecret = request.ClientSecret,
                UserCode = request.UserSecret,
                OrganizationId = request.OrganizationId
            });
        }

        [HttpPost]
        public async Task<IActionResult> GetSocialLogInEndPoint([FromBody] GetSocialLogInEndPointRequest request)
        {
            var response = await _oAuthTokenProvider.GetSocialLogInEndPointAsync(request);

            if (string.IsNullOrWhiteSpace(response.ProviderUrl))
            {
                return OAuthError.Error400Response("error", response.Error);
            }

            if (response.IsAResponse)
            {
                return Ok(response);
            }

            return Redirect(response.ProviderUrl);
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            var client = await _authenticationService.GetClientCredentialAsync(request.ClientId);

            if (client == null || client.RedirectUri != request.RedirectUri || client.Scope != request.Scope)
            {
                return Unauthorized(new { error = "authentication_failed", error_description = "Client authentication failed" });
            }

            var result = await _oAuthTokenProvider.AuthenticateAsync(new TokenRequest { GrantType = "password", Username = request.Username, Password = request.Password, Request = Request, Scope = request.Scope });

            return result is UnauthorizedObjectResult or BadRequestObjectResult ? result : Ok();
        }

        [HttpGet]
        public async Task<IActionResult> GetUserInfo()
        {
            var claimsPrincipal = await _authenticationService.GetPrincipalFromTokenAsync(Request, BlocksContext.GetContext()?.TenantId ?? "" , IsUserInfoGetRequest: false);

            if (claimsPrincipal == null)
                return Unauthorized(new { error = "invalid_token", error_description = "The access token is invalid or has expired" });

            var claims = new Dictionary<string, object?>
            {
                ["sub"] = claimsPrincipal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? null,
                ["name"] = claimsPrincipal?.FindFirst("name")?.Value ?? null,
                ["email"] = claimsPrincipal?.FindFirst(ClaimTypes.Email)?.Value ?? null,
            };

            return Ok(claims);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> UserAcknowledgement(AcknowledgeRequest request)
        {
            if (!request.IsAcknowledged)
                return Ok(new { redirectUrl = request.RedirectUri });

            if(BlocksContext.GetContext()?.UserName != request.Username)
                return BadRequest(new { error = "invalid_user", error_description = "The user is not authenticated" });

            var uri = await _authenticationService.ConstructRedirectUriAsync(request.ClientId, request);

            return Ok(new { redirectUrl = uri });
        }

        #endregion

        #region Social

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<SaveSsoCredentialResponse> SaveSsoCredential([FromBody] SaveSsoCredentialRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _authenticationDomainService.SaveSocialLoginCredentialAsync(request);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<BaseResponse> DeleteSsoCredential([FromBody] DeleteSsoCredentialRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _authenticationDomainService.DeleteSocialLoginCredentialAsync(request.ItemId);
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<GetSsoCredentialResponse> GetSsoCredential([FromQuery] GetSsoCredentialRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _authenticationDomainService.GetSsoCredentialAsync(request.ItemId);
        }


        [HttpGet]
        [ProtectedEndPoint]
        public async Task<List<SocialLoginCredential>> GetSsoCredentials([FromQuery] GetSsoCredentialsRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _authenticationDomainService.GetSocialLoginCredentialsAsync();
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<BaseResponse> UpdateStatus([FromBody] UpdateSsoCredentialStatusRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _authenticationDomainService.UpdateSsoCredentialStatusAsync(request);
        }

        [HttpGet]
        public async Task<IActionResult> GetLoginOptions()
        {
            return await _authenticationService.GetLoginOptionsAsync();
        }

        #endregion
        #region Cloud Configuration
        [ProtectedEndPoint]
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] GetAuthenticationConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _confirurationService.GetAuthenticationConfigAsync();
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<BaseResponse> Update([FromBody] UpdateAuthenticationConfigurationRequest configuration)
        {
            _changeControllerContext.ChangeContext(configuration);
            return await _confirurationService.UpdateAuthenticationConfigAsync(configuration);
        }
        #endregion
    }
}
