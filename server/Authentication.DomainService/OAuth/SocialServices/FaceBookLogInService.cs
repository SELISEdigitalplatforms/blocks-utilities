using Blocks.Genesis;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using System.Net;


namespace Authentication.DomainService.OAuth.SocialServices
{
    public class FaceBookLogInService : ISocialLogInService
    {
        private readonly ILogger<FaceBookLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;

        public FaceBookLogInService(
            ILogger<FaceBookLogInService> logger,
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
            var credential = await _authenticationRepository.GetSocialLoginCredentialByProvideAndAudienceAsync(loginData.Provider, loginData.Audience);

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
                NextUrl = loginData.NextUrl ?? string.Empty
            };

            await _cacheClient.AddStringValueAsync(stateKey, System.Text.Json.JsonSerializer.Serialize(stateInfo), 300);

            var loginUri = string.Format(
                credential.AuthorizationUrl,
                credential.ClientId,
                WebUtility.UrlEncode(credential.RedirectUrl),
                WebUtility.UrlEncode(credential.Scope),
                stateKey
            );

            return (loginUri, loginData.SendAsResponse || credential.SendAsResponse);
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var credential = await _authenticationRepository.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience);

            string faceBookGetAccessTokenUri = string.Format("{0}?client_id={1}&redirect_uri={2}&client_secret={3}&code={4}",credential.TokenUrl, credential.ClientId, credential.RedirectUrl, credential.ClientSecret, stateInfo.Code);
            _logger.LogInformation("faceBook Access Token Uri {AccessTokenUri}", faceBookGetAccessTokenUri);
            var (tokenResponse, error) = await _httpService.Get<SocialOauthAccessToken>(faceBookGetAccessTokenUri);

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("Error getting facebook access token: {Error}", error);
                return new FaceBookUserData();
            }
            var profileHeaders = new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {tokenResponse.AccessToken}" }
            };

            (var faceBookUserData, var profileError) = await _httpService.Get<FaceBookUserData>(
                credential.GetProfileUrl,
                headers: profileHeaders);

            if (!string.IsNullOrWhiteSpace(profileError))
            {
                _logger.LogError("Error fetching Facebook user profile: {ProfileError}", profileError);
                return new FaceBookUserData();
            }
            return faceBookUserData;

        }
    }
}
