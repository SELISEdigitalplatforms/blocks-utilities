using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;
using MongoDB.Driver;

namespace Iam.DomainService.Services
{
    public class IdentityAccessManagementRepository : BaseRepository, IIdentityAccessManagementRepository
    {
        public IdentityAccessManagementRepository(IDbContextProvider dbContextProvider) : base(dbContextProvider)
        {
        }

        public async Task<IamConfiguration> GetIamConfigurationAsync()
        {
            var collection = GetCollection<IamConfiguration>();

            return await collection.Find(_ => true).FirstOrDefaultAsync();
        }

        public async Task<User> GetUserByEmailAsync(string email)
        {
            var collection = GetCollection<User>();

            return await collection.Find(x => x.Email == email).FirstOrDefaultAsync();
        }

        public async Task<User> GetUserByIdAsync(string itemId)
        {
            var collection = GetCollection<User>();

            return await collection.Find(x => x.ItemId == itemId).FirstOrDefaultAsync();
        }

        public async Task<T> GetUserByIdAsync<T>(string itemId)
        {
            var collection = GetCollection<User>();
            var filter = Builders<User>.Filter.Eq(x => x.ItemId, itemId);
            var project = Builders<User>.Projection.As<T>();

            var cursor = await collection.FindAsync(filter, new FindOptions<User, T>
            {
                Projection = project
            });
            return await cursor.FirstOrDefaultAsync();
        }

        public async Task<bool> CheckPasswordBlackListedAsync(string password, string tenantId)
        {
            var collection = GetCollection<BlackListInformation>(tenantId);
            var result = await collection.CountDocumentsAsync(x => x.Key == "password" && x.Value == password);

            return result > 0;
        }

        public async Task<bool> InsertUserKeyMapAsync(UserKeyMap userKeyMap)
        {
            var collection = GetCollection<UserKeyMap>();
            await collection.InsertOneAsync(userKeyMap);

            return true;
        }

        public async Task<bool> InsertUserTimelineAsync(UserTimeline userTimeline)
        {
            var collection = GetCollection<UserTimeline>();
            await collection.InsertOneAsync(userTimeline);

            return true;
        }


        public async Task<bool> UpdateUserAsync(User user)
        {
            var collection = GetCollection<User>();
            var result = await collection.ReplaceOneAsync(x => x.ItemId == user.ItemId, user);

            return result.IsAcknowledged;
        }


        public async Task<bool> UpdateUserKeyMapActivationAsync(string userId)
        {
            var collection = GetCollection<UserKeyMap>();
            var update = Builders<UserKeyMap>.Update.Set(u => u.Activated, true);
            var result = await collection.UpdateOneAsync(u => u.UserId == userId && !u.Activated, update);

            return result.IsAcknowledged;
        }

        public async Task<List<UserKeyMap>> GetActiveUserKeyMapAsync(string userId)
        {
            var collection = GetCollection<UserKeyMap>();
            return await collection.Find(u => u.UserId == userId && !u.Activated).ToListAsync();
        }

        public async Task<string> GetUserIdFromKeyMapByKeyAsync(string key)
        {
            var collection = GetCollection<UserKeyMap>();

            // Build the filter
            var filter = Builders<UserKeyMap>.Filter.Eq(u => u.Key, key);

            // Build the projection (map to string UserId only)
            var projection = Builders<UserKeyMap>.Projection.Expression(u => u.UserId);

            // Execute FindAsync with filter + projection
            using var cursor = await collection.FindAsync(filter, new FindOptions<UserKeyMap, string>
            {
                Projection = projection
            });

            var userId = await cursor.FirstOrDefaultAsync();
            var user = await GetUserByIdAsync(userId);
            bool isActive = user?.Active ?? false;

            return !isActive ? user.ItemId : "";
        }

        public async Task<SignUpSetting> GetSingUpSettingByIdAsync(string itemId)
        {
            var collection = GetCollection<SignUpSetting>();
            return await collection.Find(x => x.ItemId == itemId).FirstOrDefaultAsync();
        }

        public async Task SaveSingUpSettingAsync(SignUpSetting signUpSetting)
        {
            var collection = GetCollection<SignUpSetting>();
            await collection.ReplaceOneAsync( x => x.ItemId == signUpSetting.ItemId, signUpSetting, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<SignUpSetting> GetSignUpSettingAsync(string? itemId = null)
        {
            var collection = GetCollection<SignUpSetting>();
            return !string.IsNullOrWhiteSpace(itemId) 
                ? await GetSingUpSettingByIdAsync(itemId)
                : await collection.Find(_ => true).FirstOrDefaultAsync();
        }

        public async Task<bool> SingnUpSettingAlreadyExist()
        {
            var collection = GetCollection<SignUpSetting>();
            var count = await collection.CountDocumentsAsync(_ => true);
            return count > 0;
        }
    }
}
