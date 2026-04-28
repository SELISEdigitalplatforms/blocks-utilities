using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users.RequestModel;
using MongoDB.Driver;

namespace Iam.DomainService.Services
{
    public interface IIdentityAccessManagementRepository
    {
        IMongoCollection<T> GetCollection<T>();
        IMongoCollection<T> GetCollection<T>(string tenantId);
        IMongoCollection<T> GetCollectionByName<T>(string collectionName);
        Task<User> GetUserByEmailAsync(string email);
        Task<User> GetUserByIdAsync(string itemId);
        Task<T> GetUserByIdAsync<T>(string itemId);
        Task<IamConfiguration> GetIamConfigurationAsync();
        Task<bool> CheckPasswordBlackListedAsync(string password, string tenantId);
        Task<bool> InsertUserKeyMapAsync(UserKeyMap userKeyMap);
        Task<bool> UpdateUserKeyMapActivationAsync(string userId);
        Task<List<UserKeyMap>> GetActiveUserKeyMapAsync(string userId);
        Task<bool> InsertUserTimelineAsync(UserTimeline userTimeline);
        Task<bool> UpdateUserAsync(User user);
        Task<string> GetUserIdFromKeyMapByKeyAsync(string key);
        Task<SignUpSetting> GetSingUpSettingByIdAsync(string itemId);
        Task SaveSingUpSettingAsync(SignUpSetting signUpSetting);
        Task<SignUpSetting> GetSignUpSettingAsync(string? itemId = null);
        Task<bool> SingnUpSettingAlreadyExist();
    }
}
