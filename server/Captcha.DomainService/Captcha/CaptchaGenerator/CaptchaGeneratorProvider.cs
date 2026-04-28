using Blocks.Genesis;
using MongoDB.Driver;
using Captcha.DomainService.Configuration;

namespace Captcha.DomainService.Captcha
{
    public class CaptchaGeneratorProvider : ICaptchaGeneratorProvider
    {
        private readonly string _collectionName = "CaptchaConfigurations";
        private readonly IDbContextProvider _dbContextProvider;

        private static readonly IDictionary<string, ICaptchaGenerator> CaptchaGenerators = new Dictionary<string, ICaptchaGenerator>
        {
            { nameof(EasyCaptchaGenerator).ToLower(), new EasyCaptchaGenerator() },
            { nameof(HardCaptchaGenerator).ToLower(), new HardCaptchaGenerator() }
        };

        public CaptchaGeneratorProvider(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public ICaptchaGenerator GetCaptchaGenerator(string configurationName)
        {
            string generatorName = GetGeneratorName(configurationName);
            return CaptchaGenerators[generatorName];
        }

        public virtual string GetGeneratorName(string configurationName)
        {
            var collection = _dbContextProvider.GetCollection<CaptchaConfiguration>(_collectionName);
            var filter = Builders<CaptchaConfiguration>.Filter.Eq(mc => mc.Provider, configurationName);
            var setting = collection.Find(filter).FirstOrDefault();
            var generatorName = setting == null ? nameof(HardCaptchaGenerator) : setting.CaptchaGenerator.ToString();

            return generatorName.ToLower();
        }
    }
}
