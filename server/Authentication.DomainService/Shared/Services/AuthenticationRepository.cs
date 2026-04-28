using Blocks.Genesis;
using DomainService.Entities;
using DomainService.RequestModel;
using DomainService.Shared.RequestModel;
using DomainService.Shared.ResponseModel;
using Iam.DomainService.Entities;
using MongoDB.Driver;

namespace DomainService.Services
{
    public class AuthenticationRepository : IAuthenticationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        public AuthenticationRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public IMongoCollection<T> GetCollection<T>()
        {
            return _dbContextProvider.GetCollection<T>($"{typeof(T).Name}s");
        }

        public IMongoCollection<T> GetCollection<T>(string tenantId)
        {
            return _dbContextProvider.GetCollection<T>(tenantId, $"{typeof(T).Name}s");
        }

        public IMongoCollection<T> GetCollectionByName<T>(string collectionName)
        {
            return _dbContextProvider.GetCollection<T>(collectionName);
        }

        public async Task<User> GetUserByEmailAsync(string email)
        {
            var collection = GetCollection<User>();
            var options = new FindOptions<User>
            {
                Collation = new Collation("en", strength: CollationStrength.Secondary)
            };
            var filter = Builders<User>.Filter.Eq(x => x.Email, email);
            return await (await collection.FindAsync(filter, options)).FirstOrDefaultAsync();
        }

        public async Task<User> GetUserByUsernameAsync(string username, string? organizationId = null)
        {
            var collection = GetCollection<User>();

            if (!string.IsNullOrWhiteSpace(organizationId))
            {
                var filter = Builders<User>.Filter.And(
                             Builders<User>.Filter.Eq(u => u.UserName, username),
                             Builders<User>.Filter.AnyEq("OrganizationIds", organizationId));

                return await collection.Find(filter).FirstOrDefaultAsync();
            }

            return await collection.Find(x => x.UserName == username).FirstOrDefaultAsync();
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

        public async Task<bool> InsertSessionAsync(Session session)
        {
            var collection = GetCollection<Session>();
            await collection.InsertOneAsync(session);
            return true;
        }

        public async Task<bool> InsertUserAuthenticationTimelineAsync(UserAuthenticationTimeline userAuthenticationTimeline)
        {
            var collection = GetCollection<UserAuthenticationTimeline>();
            await collection.InsertOneAsync(userAuthenticationTimeline);
            return true;
        }

        public async Task<bool> UpdateSessionStatusAsync(string refreshToken, string userId)
        {
            var collection = GetCollection<Session>();
            var update = Builders<Session>.Update.Set(x => x.IsActive, false)
                .Set(x => x.ExpiresUtc, DateTime.UtcNow)
                .Set(x => x.UpdateDate, DateTime.Now);
            var result = await collection.UpdateManyAsync(x => x.RefreshToken == refreshToken && x.UserId == userId, update);
            return result.IsAcknowledged;
        }

        public async Task<bool> UpdateSessionStatusForAllRefreshTokenAsync(IEnumerable<string> refreshTokens)
        {
            var collection = GetCollection<Session>();
            var update = Builders<Session>.Update.Set(x => x.IsActive, false)
                .Set(x => x.ExpiresUtc, DateTime.UtcNow)
                .Set(x => x.UpdateDate, DateTime.Now);
            var filter = Builders<Session>.Filter.In(x => x.RefreshToken, refreshTokens);
            var result = await collection.UpdateManyAsync(filter, update);
            return result.IsAcknowledged;
        }

        public async Task<IEnumerable<Session>> GetActiveSessionByUserIdAsync(string userId)
        {
            var collection = GetCollection<Session>();
            var filter = Builders<Session>.Filter.Eq(x => x.UserId, userId) & Builders<Session>.Filter.Eq(x => x.IsActive, true);
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<IEnumerable<SocialLoginCredential>> GetSocialLoginCredentials()
        {
            var collection = GetCollection<SocialLoginCredential>();
            return await collection.Find(_ => true).ToListAsync();
        }

        public async Task<SocialLoginCredential> GetSocialLoginCredentialByProvideAndAudienceAsync(string provider, string audience)
        {
            var collection = GetCollection<SocialLoginCredential>();
            var filter = Builders<SocialLoginCredential>.Filter.Eq(x => x.Provider, provider) & Builders<SocialLoginCredential>.Filter.Eq(x => x.Audience, audience);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<SocialLoginCredential> GetSocialLoginCredentialByIdAsync(string itemId)
        {
            var collection = GetCollection<SocialLoginCredential>();
            var filter = Builders<SocialLoginCredential>.Filter.Eq(x => x.ItemId, itemId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<SocialLoginCredential>> GetSocialLoginCredentialsAsync()
        {
            var collection = GetCollection<SocialLoginCredential>();
            var filter = Builders<SocialLoginCredential>.Filter.Where(_ => true);
            var cursor = await collection.FindAsync(filter);
            return await cursor.ToListAsync();
        }

        public async Task<bool> SaveSocialLoginCredentialAsync(SocialLoginCredential socialLoginCredential)
        {
            var collection = GetCollection<SocialLoginCredential>();
            var result = await collection.ReplaceOneAsync(x => x.ItemId == socialLoginCredential.ItemId, socialLoginCredential, new ReplaceOptions { IsUpsert = true });
            return result.IsAcknowledged;
        }

        public async Task<bool> DeleteSocialLoginCredentialAsync(string itemId)
        {
            var collection = GetCollection<SocialLoginCredential>();
            var result = await collection.DeleteOneAsync(x => x.ItemId == itemId);
            return result.IsAcknowledged;
        }

        public async Task UpdatePartialAsync<T>(string id, Dictionary<string, object> updates, string collectionName = "")
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);

            var filter = Builders<T>.Filter.Eq("_id", id);
            var updateDefinition = new List<UpdateDefinition<T>>();

            foreach (var update in updates)
            {
                updateDefinition.Add(Builders<T>.Update.Set(update.Key, update.Value));
            }

            var combinedUpdate = Builders<T>.Update.Combine(updateDefinition);
            await collection.UpdateOneAsync(filter, combinedUpdate);
        }

        public async Task<AuthenticationConfiguration> GetAuthenticationConfigurationAsync()
        {
            var collection = GetCollection<AuthenticationConfiguration>();
            var filter = Builders<AuthenticationConfiguration>.Filter.Where(_ => true);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task UpdateAuthenticationConfigurationAsync(AuthenticationConfiguration authenticationConfiguration)
        {
            var collection = GetCollection<AuthenticationConfiguration>();
            var filter = Builders<AuthenticationConfiguration>.Filter.Eq("_id", authenticationConfiguration.ItemId);
            await collection.ReplaceOneAsync(filter, authenticationConfiguration);
        }

        public async Task<OIDCClientCredential> GetOIDCClientCredentialAsync(string clientId)
        {
            var collection = GetCollection<OIDCClientCredential>();
            var filter = Builders<OIDCClientCredential>.Filter.Eq(x => x.ItemId, clientId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task SaveOIDCClientCredentialAsync(OIDCClientCredential credential)
        {
            var collection = GetCollection<OIDCClientCredential>();
            var result = await collection.ReplaceOneAsync(x => x.ItemId == credential.ItemId, credential, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<OIDCClientCredential> GetOIDCCredentialByIdAsync(string itemId)
        {
            var collection = GetCollection<OIDCClientCredential>();
            var filter = Builders<OIDCClientCredential>.Filter.Eq(it => it.ItemId, itemId);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<List<OIDCClientCredential>> GetOIDCCredentialsByTenantAsync()
        {
            var collection = GetCollection<OIDCClientCredential>();
            var filter = Builders<OIDCClientCredential>.Filter.Empty;
            return await (await collection.FindAsync(filter)).ToListAsync();
        }

        public async Task DeleteOidcCliantAsync(DeleteOIDCClientRequest request)
        {
            var collection = GetCollection<OIDCClientCredential>();
            var filter = Builders<OIDCClientCredential>.Filter.Eq(it => it.ItemId, request.ItemId);
            await collection.DeleteOneAsync(filter);
        }

        public async Task<ClientCredential> GetClientCredentialByIdAsync(string clientId)
        {
            var collection = GetCollection<ClientCredential>();
            var filter = Builders<ClientCredential>.Filter.Eq(it => it.ItemId, clientId);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<BiometricCredential> AuthenticateBiometricCredentialAsync(string biometricId, string biometricKey)
        {
            var collection = GetCollection<BiometricCredential>();
            var filter = Builders<BiometricCredential>.Filter.Where(it => it.BiometricId == biometricId && it.BiometriKey == biometricId);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<UserCode> GetUserCodeAsync(string code)
        {
            var collection = GetCollection<UserCode>();
            var filter = Builders<UserCode>.Filter.Eq(it => it.Code, code);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<BlocksClientConfig> GetBlocksClientAsync(string clientId)
        {
            var collection = GetCollection<BlocksClientConfig>();
            var filter = Builders<BlocksClientConfig>.Filter.Eq(it => it.ItemId, clientId);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task SaveUserCodeByClientAsync(UserCode userCode)
        {
            var collection = GetCollection<UserCode>();
            var result = await collection.ReplaceOneAsync(x => x.ItemId == userCode.ItemId, userCode, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<List<GetUserCodesByUserIdResponse>> GetUserCodesByUserIdAsync(string userId)
        {
            var collection = GetCollection<UserCode>();
            var filter = Builders<UserCode>.Filter.Eq(it => it.UserId, userId);
            var userCodes = await (await collection.FindAsync(filter)).ToListAsync();
            return GetUserCodesByUserIdResponse(userCodes);
        }

        private List<GetUserCodesByUserIdResponse> GetUserCodesByUserIdResponse(List<UserCode> userCodes)
        {
            return userCodes.Select(x => new GetUserCodesByUserIdResponse
            {
                ItemId = x.ItemId,
                CreatedDate = x.CreatedDate,
                UserId = x.UserId,
                Code = x.Code,
                ExpiryDate = x.CodeTtlInMinute.HasValue ? x.CreatedDate.AddMinutes(x.CodeTtlInMinute.Value) : x.CreatedDate,
                CodeTtlInMinute = x.CodeTtlInMinute,
                ClientId = x.ClientId,
                Note = x.Note,
            }).ToList();
        }

        public async Task<BaseResponse> SaveClientCredentialAsync(ClientCredential clientCredential)
        {
            var collection = GetCollection<ClientCredential>();
            var result = await collection.ReplaceOneAsync(x => x.ItemId == clientCredential.ItemId, clientCredential, new ReplaceOptions { IsUpsert = true });
            return new BaseResponse { IsSuccess = result.IsAcknowledged };
        }

        public async Task DeleteClientCredentialAsync(DeleteClientCredentialRequest request)
        {
            var collection = GetCollection<ClientCredential>();
            var filter = Builders<ClientCredential>.Filter.Eq(it => it.ItemId, request.ItemId);
            await collection.DeleteOneAsync(filter);
        }

        public async Task<List<ClientCredential>> GetClientCredentialsAsync()
        {
            var collection = GetCollection<ClientCredential>();
            var filter = Builders<ClientCredential>.Filter.Where(_ => true);
            var cursor = await collection.FindAsync(filter);
            return await cursor.ToListAsync();
        }
    }
}