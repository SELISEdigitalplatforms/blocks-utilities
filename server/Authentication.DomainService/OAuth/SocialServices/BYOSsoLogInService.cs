using Blocks.Genesis;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

namespace DomainService.OAuth
{
    public class BYOSsoLogInService : SocialLogInServiceBase
    {
        public BYOSsoLogInService(
            ILogger<BYOSsoLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService
        ) : base(logger, authenticationRepository, cacheClient, httpService)
        {
        }

        public override async Task<(string, bool)> GetProviderLogInUriAsync(GetSocialLogInEndPointRequest loginData)
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

            await _cacheClient.AddStringValueAsync(socialLogInStateKey, JsonSerializer.Serialize(socialLogInStateInfo), 3000);

            var redirectUri = $"{credential.AuthorizationUrl}&response_type=code&client_id={credential.ClientId}&state={socialLogInStateKey}&redirect_uri={credential.RedirectUrl}&scope=openid";

            return (redirectUri, credential.SendAsResponse);
        }

        public override async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var credential = await _authenticationRepository.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience);
            var postData = new Dictionary<string, string>
                {
                    { "code", stateInfo.Code },
                    { "client_id", credential.ClientId },
                    { "client_secret", credential.ClientSecret },
                    { "redirect_uri", credential.RedirectUrl },
                    { "grant_type", "authorization_code" }
                };

            var (response, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(HttpMethod.Post, postData, credential.TokenUrl);
            _logger.LogInformation("access token: {Response}", response);
            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("Error while getting access token: {Error}", error);
                return new BYOSsoUserData();
            }

            var result = await _httpService.Get<dynamic>(credential.GetProfileUrl, new Dictionary<string, string> {
                { "Authorization", $"bearer {response.AccessToken}"  } });

            if (!string.IsNullOrWhiteSpace(result.Item2))
            {
                _logger.LogError("Error while getting user data: {Error}", result.Item2);
                return new BYOSsoUserData();
            }

            var externalUser = MapExternalUser(stateInfo.Provider, result.Item1);
            externalUser.Permissions = credential?.InitialPermissions ?? [];
            externalUser.Roles = credential?.InitialRoles ?? [];
            externalUser.Platform = stateInfo.Provider;

            return externalUser;
        }

        private static BYOSsoUserData MapExternalUser(string provider, dynamic result)
        {
            var user = new BYOSsoUserData { };
            
            switch (provider.ToLower())
            {
                case SocialLogInTypes.AzureAd:
                case SocialLogInTypes.WindowsLive:
                case SocialLogInTypes.Microsoft:
                    if (result.TryGetProperty("oid", out JsonElement oid))
                    {
                        user.ExternalProviderUserId = oid.ToString();
                    }
                    else
                    {
                        user.ExternalProviderUserId = result.TryGetProperty("sub", out JsonElement sub1) ? sub1.ToString() : "";
                    }

                    if (result.TryGetProperty("preferred_username", out JsonElement preferredUsername))
                    {
                        user.Email = preferredUsername.ToString();
                    }
                    else
                    {
                        user.Email = result.TryGetProperty("email", out JsonElement email1) ? email1.ToString() : "";
                    }

                    user.DisplayName = result.TryGetProperty("name", out JsonElement name1) ? name1.ToString() : "";
                    user.FirstName = result.TryGetProperty("given_name", out JsonElement givenName1) ? givenName1.ToString() : "";
                    user.LastName = result.TryGetProperty("family_name", out JsonElement familyName1) ? familyName1.ToString() : "";
                    break;

                case SocialLogInTypes.Okta:
                    user.ExternalProviderUserId = result.TryGetProperty("sub", out JsonElement sub2) ? sub2.ToString() : "";
                    user.Email = result.TryGetProperty("email", out JsonElement email2) ? email2.ToString() : "";
                    user.DisplayName = result.TryGetProperty("name", out JsonElement name2) ? name2.ToString() : "";
                    user.FirstName = result.TryGetProperty("given_name", out JsonElement givenName2) ? givenName2.ToString() : "";
                    user.LastName = result.TryGetProperty("family_name", out JsonElement familyName2) ? familyName2.ToString() : "";
                    break;

                case SocialLogInTypes.Google:
                    user.ExternalProviderUserId = result.TryGetProperty("sub", out JsonElement sub3) ? sub3.ToString() : "";
                    user.Email = result.TryGetProperty("email", out JsonElement email3) ? email3.ToString() : "";
                    user.DisplayName = result.TryGetProperty("name", out JsonElement name3) ? name3.ToString() : "";
                    user.FirstName = result.TryGetProperty("given_name", out JsonElement givenName3) ? givenName3.ToString() : "";
                    user.LastName = result.TryGetProperty("family_name", out JsonElement familyName3) ? familyName3.ToString() : "";
                    user.ProfileImageUrl = result.TryGetProperty("picture", out JsonElement picture1) ? picture1.ToString() : "";
                    break;

                case SocialLogInTypes.Github:
                    user.ExternalProviderUserId = result.TryGetProperty("id", out JsonElement id1) ? id1.ToString() : "";
                    user.Email = result.TryGetProperty("email", out JsonElement email4) ? email4.ToString() : "";
                    user.DisplayName = result.TryGetProperty("login", out JsonElement login1) ? login1.ToString() : "";
                    user.ProfileImageUrl = result.TryGetProperty("avatar_url", out JsonElement avatar1) ? avatar1.ToString() : "";
                    break;

                case SocialLogInTypes.FaceBook:
                    user.ExternalProviderUserId = result.TryGetProperty("id", out JsonElement id2) ? id2.ToString() : "";
                    user.Email = result.TryGetProperty("email", out JsonElement email5) ? email5.ToString() : "";
                    user.DisplayName = result.TryGetProperty("name", out JsonElement name4) ? name4.ToString() : "";
                    user.ProfileImageUrl = result.TryGetProperty("picture", out JsonElement picture2) ? picture2.ToString() : "";
                    break;

                case SocialLogInTypes.LinkedIn:
                    user.ExternalProviderUserId = result.TryGetProperty("id", out JsonElement id3) ? id3.ToString() : "";
                    user.FirstName = result.TryGetProperty("localizedFirstName", out JsonElement firstName1) ? firstName1.ToString() : "";
                    user.LastName = result.TryGetProperty("localizedLastName", out JsonElement lastName1) ? lastName1.ToString() : "";
                    user.DisplayName = $"{user.FirstName} {user.LastName}".Trim();
                    user.Email = result.TryGetProperty("email", out JsonElement email6) ? email6.ToString() : "";
                    user.ProfileImageUrl = result.TryGetProperty("profilePicture", out JsonElement picture3) ? picture3.ToString() : "";
                    break;

                case SocialLogInTypes.KeyCloak:
                    user.ExternalProviderUserId = result.TryGetProperty("sub", out JsonElement sub4) ? sub4.ToString() : "";
                    user.Email = result.TryGetProperty("email", out JsonElement email7) ? email7.ToString() : "";
                    user.DisplayName = result.TryGetProperty("name", out JsonElement name5) ? name5.ToString() : result.TryGetProperty("preferred_username", out JsonElement preferred1) ? preferred1.ToString() : "";
                    break;

                case SocialLogInTypes.Ping:
                    user.ExternalProviderUserId = result.TryGetProperty("sub", out JsonElement sub5) ? sub5.ToString() : "";
                    user.Email = result.TryGetProperty("email", out JsonElement email8) ? email8.ToString() : "";
                    user.DisplayName = result.TryGetProperty("name", out JsonElement name6) ? name6.ToString() : "";
                    break;

                case SocialLogInTypes.Adfs:
                    if (result.TryGetProperty("nameid", out JsonElement nameId1))
                    {
                        user.ExternalProviderUserId = nameId1.ToString();
                    }
                    else
                    {
                        user.ExternalProviderUserId = result.TryGetProperty("sub", out JsonElement sub6) ? sub6.ToString() : "";
                    }

                    if (result.TryGetProperty("upn", out JsonElement upn1))
                    {
                        user.Email = upn1.ToString();
                    }
                    else
                    {
                        user.Email = result.TryGetProperty("email", out JsonElement email9) ? email9.ToString() : "";
                    }

                    user.DisplayName = result.TryGetProperty("displayname", out JsonElement display1) ? display1.ToString() : "";
                    break;

                default: // fallback for any other BYOSSO / custom Auth0 connections
                    user.ExternalProviderUserId = result.TryGetProperty("sub", out JsonElement sub7) ? sub7.ToString() : "";
                    user.Email = result.TryGetProperty("email", out JsonElement email10) ? email10.ToString() : "";
                    user.DisplayName = result.TryGetProperty("name", out JsonElement name7) ? name7.ToString() : "";
                    user.FirstName = result.TryGetProperty("given_name", out JsonElement givenName4) ? givenName4.ToString() : "";
                    user.LastName = result.TryGetProperty("family_name", out JsonElement familyName4) ? familyName4.ToString() : "";
                    user.ProfileImageUrl = result.TryGetProperty("picture", out JsonElement picture4) ? picture4.ToString() : "";
                    user.PhoneNumber = result.TryGetProperty("phone_number", out JsonElement phone1) ? phone1.ToString() : "";
                    break;
            }

            return user;
        }

        protected override IExternalUserData CreateEmptyUserData()
        {
            return new BYOSsoUserData();
        }
    }

}
