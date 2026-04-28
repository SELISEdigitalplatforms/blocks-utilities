using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;


namespace Authentication.DomainService.OAuth.SocialServices
{
    public class AppleLogInService : ISocialLogInService
    {
        private readonly ILogger<AppleLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;

        public AppleLogInService(
            ILogger<AppleLogInService> logger,
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
            await _cacheClient.AddStringValueAsync(
                stateKey,
                System.Text.Json.JsonSerializer.Serialize(stateInfo),
                300
            );

            var authorizationUrl = string.Format(
                credential.AuthorizationUrl,
                credential.ClientId,                              
                WebUtility.UrlEncode(credential.Scope),           
                WebUtility.UrlEncode(credential.RedirectUrl),     
                stateKey                                          
            );

            return (authorizationUrl, loginData.SendAsResponse || credential.SendAsResponse);
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var credential = await _authenticationRepository.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience);
            var postData = new Dictionary<string, string>
                {
                    { "code", stateInfo.Code },
                    { "client_id", credential.ClientId },
                    { "client_secret",  GenerateClientSecret(credential)},
                    { "redirect_uri", credential.RedirectUrl },
                    { "grant_type", "authorization_code" }
                };

            var (response, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(HttpMethod.Post, postData, credential.TokenUrl);

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("Error while getting access token: {Error}", error);
                return new AppleUserData();
            }

            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(response.IdToken);
            var payload = JsonConvert.SerializeObject(jwtToken.Payload);
            var deserializeAppleIdToken = JsonConvert.DeserializeObject<AppleIdToken>(payload);
            var appleUserData = new AppleUserData();
            appleUserData.Email = deserializeAppleIdToken.Email;
            appleUserData.ExternalProviderUserId = deserializeAppleIdToken.ExternalProviderUserId;
            appleUserData.Roles = credential?.InitialRoles ?? [];
            appleUserData.Platform = stateInfo.Provider;
            return appleUserData;
        }
        public string GenerateClientSecret(SocialLoginCredential socialLoginCredential)
        {
            string teamId = socialLoginCredential.TeamId;
            string clientId = socialLoginCredential.ClientId;
            string keyId = socialLoginCredential.KeyId;
            var privateKey = socialLoginCredential.PrivateKey;
            var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(privateKey);
            var securityKey = new ECDsaSecurityKey(ecdsa)
            {
                KeyId = keyId
            };
            var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var payload = new JwtPayload
            {
                { "iss", teamId },
                { "iat", now },
                { "exp", now + 300 },
                { "aud", socialLoginCredential.AppleAudience },
                { "sub", clientId }
            };
            var header = new JwtHeader(signingCredentials);
            var jwt = new JwtSecurityToken(header, payload);
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

    }
}
