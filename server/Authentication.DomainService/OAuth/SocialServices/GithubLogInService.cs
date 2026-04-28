using Blocks.Genesis;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace DomainService.OAuth
{
    public class GithubLogInService : ISocialLogInService
    {
        private readonly ILogger<GithubLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;

        public GithubLogInService(
            ILogger<GithubLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService)
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
            _cacheClient = cacheClient;
            _httpService = httpService;
        }

        public async Task<(string, bool)> GetProviderLogInUriAsync(GetSocialLogInEndPointRequest loginData)
        {
            var credential = await _authenticationRepository
                .GetSocialLoginCredentialByProvideAndAudienceAsync(loginData.Provider, loginData.Audience);

            if (credential == null)
            {
                _logger.LogError("Credential not found for provider {Provider} and audience {Audience}", loginData.Provider, loginData.Audience);
                return (string.Empty, true);
            }

            var stateKey = Guid.NewGuid().ToString("n");
            var stateInfo = new StateInfo
            {
                Audience = loginData.Audience,
                Provider = loginData.Provider,
                NextUrl = loginData.NextUrl,
            };

            await _cacheClient.AddStringValueAsync(stateKey, JsonSerializer.Serialize(stateInfo), 300);

            // GitHub auth URL 
            var loginUri = $"{credential.AuthorizationUrl.Split("?")[0]}?scope={credential.Scope}&state={stateKey}&redirect_uri={WebUtility.UrlEncode(credential.RedirectUrl)}&client_id={credential.ClientId}&response_type=code";

            return (loginUri, loginData.SendAsResponse || credential.SendAsResponse);
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var credential = await _authenticationRepository
                .GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience);

            var postData = new Dictionary<string, string>
            {
                { "code", stateInfo.Code },
                { "client_id", credential.ClientId },
                { "client_secret", credential.ClientSecret },
                { "redirect_uri", credential.RedirectUrl }
            };

            // Ask for JSON response
            var (tokenResponse, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(
                HttpMethod.Post,
                postData,
                credential.TokenUrl,
                headers: new Dictionary<string, string> { { "Accept", "application/json" } });

            if (!string.IsNullOrWhiteSpace(error) || tokenResponse?.AccessToken == null)
            {
                _logger.LogError("Error while getting GitHub access token: {Error}", error);
                return new GithubUserData();
            }

            // Fetch GitHub user profile
            var (userResponse, userError) = await _httpService.Get<GithubUserData>(
                credential.GetProfileUrl,
                headers: new Dictionary<string, string> { { "Authorization", $"Bearer {tokenResponse.AccessToken}" }, { "User-Agent", credential.Audience } });

            if (!string.IsNullOrWhiteSpace(userError))
            {
                _logger.LogError("Error while getting GitHub user data: {UserError}", userError);
                return new GithubUserData();
            }

            if (string.IsNullOrEmpty(userResponse.Email))
            {
                var (emailResponse, emailError) = await _httpService.Get<List<GithubEmail>>(
                credential.GetEmailUrl,
                headers: new Dictionary<string, string> { { "Authorization", $"Bearer {tokenResponse.AccessToken}" },
                { "User-Agent", $"{credential.Audience}" },{ "Accept", "application/vnd.github.v3+json" } });

                if (!string.IsNullOrWhiteSpace(emailError) || emailResponse == null)
                {
                    _logger.LogError("Error while getting GitHub user email: {EmailError}", emailError);
                    return userResponse;
                }
                userResponse.Email = emailResponse.FirstOrDefault(e => e.Primary && e.Verified)?.Email
                       ?? emailResponse.FirstOrDefault(e => e.Verified)?.Email
                       ?? emailResponse.FirstOrDefault()?.Email;
            }

            userResponse.ExternalProviderUserId = userResponse.Id.ToString();
            userResponse.Permissions = credential?.InitialPermissions ?? [];
            userResponse.Roles = credential?.InitialRoles ?? [];
            userResponse.Platform = stateInfo.Provider;

            return userResponse;
        }
    }
}
