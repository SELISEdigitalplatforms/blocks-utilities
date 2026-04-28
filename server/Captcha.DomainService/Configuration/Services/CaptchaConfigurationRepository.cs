using Blocks.Genesis;
using MongoDB.Driver;

namespace Captcha.DomainService.Configuration
{
    public class CaptchaConfigurationRepository : ICaptchaConfigurationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "CaptchaConfigurations";

        public CaptchaConfigurationRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task<CaptchaConfiguration> GetByProviderAsync(string provider)
        {
            var collection = _dbContextProvider.GetCollection<CaptchaConfiguration>(_collectionName);
            var filter = Builders<CaptchaConfiguration>.Filter.Eq(mc => mc.Provider, provider);

            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<CaptchaConfiguration> GetCaptchaConfigurationAsync()
        {
            var collection = _dbContextProvider.GetCollection<CaptchaConfiguration>(_collectionName);
            return await (await collection.FindAsync(Builders<CaptchaConfiguration>.Filter.Eq(mc => mc.IsEnable, true))).FirstOrDefaultAsync();
        }
    }
}
