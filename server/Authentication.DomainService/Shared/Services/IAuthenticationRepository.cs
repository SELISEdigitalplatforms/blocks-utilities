using Blocks.Genesis;
using DomainService.Authentication;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.Services;
using DomainService.RequestModel;
using DomainService.Shared.RequestModel;
using DomainService.Shared.ResponseModel;
using Iam.DomainService.Entities;
using MongoDB.Driver;

namespace DomainService.Services
{
    public interface IAuthenticationRepository
    {
        IMongoCollection<T> GetCollection<T>();
        IMongoCollection<T> GetCollection<T>(string tenantId);
        IMongoCollection<T> GetCollectionByName<T>(string collectionName);
        Task<User> GetUserByEmailAsync(string email);
        Task<User> GetUserByUsernameAsync(string username, string? organizationId = null);
        Task<User> GetUserByIdAsync(string itemId);
        Task<T> GetUserByIdAsync<T>(string itemId);
        Task<bool> InsertSessionAsync(Session session);
        Task<bool> InsertUserAuthenticationTimelineAsync(UserAuthenticationTimeline userAuthenticationTimeline);
        Task<IEnumerable<Session>> GetActiveSessionByUserIdAsync(string userId);
        Task<bool> UpdateSessionStatusForAllRefreshTokenAsync(IEnumerable<string> refreshTokens);
        Task<bool> UpdateSessionStatusAsync(string refreshToken, string userId);
        Task<IEnumerable<SocialLoginCredential>> GetSocialLoginCredentials();
        Task<SocialLoginCredential> GetSocialLoginCredentialByProvideAndAudienceAsync(string provider, string audience);
        Task<bool> SaveSocialLoginCredentialAsync(SocialLoginCredential socialLoginCredential);
        Task<bool> DeleteSocialLoginCredentialAsync(string itemId);
        Task<SocialLoginCredential> GetSocialLoginCredentialByIdAsync(string itemId);
        Task UpdatePartialAsync<T>(string id, Dictionary<string, object> updates, string collectionName = "");
        Task<List<SocialLoginCredential>> GetSocialLoginCredentialsAsync();
        Task<AuthenticationConfiguration> GetAuthenticationConfigurationAsync();
        Task UpdateAuthenticationConfigurationAsync(AuthenticationConfiguration authenticationConfiguration);
        Task<OIDCClientCredential> GetOIDCClientCredentialAsync(string clientId);
        Task<List<OIDCClientCredential>> GetOIDCCredentialsByTenantAsync();
        Task SaveOIDCClientCredentialAsync(OIDCClientCredential credential);
        Task<OIDCClientCredential> GetOIDCCredentialByIdAsync(string tenantId);
        Task DeleteOidcCliantAsync(DeleteOIDCClientRequest request);
        Task<BiometricCredential> AuthenticateBiometricCredentialAsync(string biometricId, string biometricKey);
        Task<ClientCredential> GetClientCredentialByIdAsync(string clientId);
        Task<UserCode> GetUserCodeAsync(string code);
        Task<BlocksClientConfig> GetBlocksClientAsync(string clientId);
        Task SaveUserCodeByClientAsync(UserCode userCode);
        Task<List<GetUserCodesByUserIdResponse>> GetUserCodesByUserIdAsync(string userId);
        Task<BaseResponse> SaveClientCredentialAsync(ClientCredential clientCredential);
        Task DeleteClientCredentialAsync(DeleteClientCredentialRequest request);
        Task<List<ClientCredential>> GetClientCredentialsAsync();
    }
}
