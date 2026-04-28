using Azure;
using Blocks.Genesis;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Azure.Amqp.Framing;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs.Kerberos;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using static QRCoder.PayloadGenerator;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace DomainService.OAuth
{
    public abstract class SocialLogInServiceBase : ISocialLogInService
    {
        protected readonly ILogger _logger;
        protected readonly IAuthenticationRepository _authenticationRepository;
        protected readonly ICacheClient _cacheClient;
        protected readonly IHttpService _httpService;
        private const string googleProvider = "google";
        private const string microsoftProvider = "microsoft";

        protected SocialLogInServiceBase(
            ILogger logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService
        )
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
            _cacheClient = cacheClient;
            _httpService = httpService;
        }

        public virtual async Task<(string, bool)> GetProviderLogInUriAsync(GetSocialLogInEndPointRequest loginData)
        {
            var credential = await _authenticationRepository.GetSocialLoginCredentialByProvideAndAudienceAsync(loginData.Provider, loginData.Audience);

            if (credential == null)
            {
                _logger.LogError("Credential not found for provider {Provider} and audience {Audience}", loginData.Provider, loginData.Audience);
                return (string.Empty, true);
            }

            var socialLogInStateKey = Guid.NewGuid().ToString("n");
            var socialLogInStateInfo = new StateInfo
            {
                Audience = loginData.Audience,
                Provider = loginData.Provider,
                NextUrl = loginData.NextUrl,
            };

            await _cacheClient.AddStringValueAsync(socialLogInStateKey, JsonSerializer.Serialize(socialLogInStateInfo), 300);

            return (string.Format(credential.AuthorizationUrl, credential.Scope, socialLogInStateKey, WebUtility.UrlEncode(credential.RedirectUrl), credential.ClientId), loginData.SendAsResponse || credential.SendAsResponse);
        }

        public virtual async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var credential = await _authenticationRepository.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience);

            var postData = new Dictionary<string, string>
            {
                { "code", stateInfo.Code },
                { "client_id", credential.ClientId },
                { "client_secret", credential.ClientSecret },
                { "redirect_uri", credential.RedirectUrl },
                { "grant_type", "authorization_code" },
                { "scope", "openid profile email" }
            };

            var (response, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(HttpMethod.Post, postData, credential.TokenUrl);

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("Error while getting access token: {Error}", error);
                return CreateEmptyUserData();
            }
            var externalUser = stateInfo.Provider switch
            {
                googleProvider => await GetGoogleProfileVerification(credential.GetProfileUrl, response.AccessToken),
                microsoftProvider => await GetMicrosoftProfileVerification(credential.GetProfileUrl, response.AccessToken, response.IdToken),
                _ => CreateEmptyUserData()
            };

            externalUser.Permissions = credential.InitialPermissions;

            Console.WriteLine($"IntraId Roles: {string.Join(", ", externalUser.Roles)}");

            if (externalUser.Roles.Count > 0)
                externalUser.Roles.AddRange(credential.InitialRoles);
            else
                externalUser.Roles = credential.InitialRoles;

            externalUser.Platform = stateInfo.Provider;

            return externalUser;
        }

        protected abstract IExternalUserData CreateEmptyUserData();
        private async Task<IExternalUserData> GetGoogleProfileVerification(string profileURL, string accessToken)
        {
            var userAccessEndPoint = string.Format(profileURL, accessToken);

            var( externalUser, error) = await _httpService.Get<GoogleUserData>(userAccessEndPoint);

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("Error while getting google user data: {Error}", error);
                return new GoogleUserData();
            }
            if (!string.IsNullOrWhiteSpace(error) || externalUser == null)
            {
                _logger.LogError("Error while getting user data: {Error}", error);
                return CreateEmptyUserData();
            }

            return externalUser;
        }
        private async Task<IExternalUserData> GetMicrosoftProfileVerification(string profileURL, string accessToken, string idToken)
        {
            var queryPram = "$select=displayName,mail,department,employeeId,givenName,userPrincipalName,surname,officeLocation,preferredLanguage,mobilePhone,id";
            profileURL = $"{profileURL}?{queryPram}";

            var (externalUser, error) = await _httpService.Get<MicrosoftUserData>(profileURL , new Dictionary<string, string> {
                { "Authorization", $"bearer {accessToken}"  }
            });

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("Error while getting microsoft user data: {Error}", error);
                return new MicrosoftUserData();
            }
            if (!string.IsNullOrWhiteSpace(error) || externalUser == null)
            {
                _logger.LogError("Error while getting user data: {Error}", error);
                return CreateEmptyUserData();
            }

            externalUser.Roles = ExtractRolesFromJwt(idToken);

            return externalUser;
        }


        public static List<string> ExtractRolesFromJwt(string jwt)
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwt); // NOTE: this does NOT validate signature

            // Look for "roles" claim (your token uses "roles")
            var rolesClaim = token.Claims.FirstOrDefault(c => c.Type == "roles")?.Value;

            if (string.IsNullOrWhiteSpace(rolesClaim))
                return new List<string>();

            // If roles is a JSON array, parse it; otherwise treat as single role string
            rolesClaim = rolesClaim.Trim();

            if (rolesClaim.StartsWith("["))
            {
                return JsonSerializer.Deserialize<List<string>>(rolesClaim) ?? new List<string>();
            }

            return new List<string> { rolesClaim };
        }
    }
}