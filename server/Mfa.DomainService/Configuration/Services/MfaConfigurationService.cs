using Mfa.DomainService.Services;

namespace Mfa.DomainService.Configuration
{
    public class MfaConfigurationService : IMfaConfigurationService
    {
        private readonly IMfaManagementRepository _repository;

        public MfaConfigurationService(IMfaManagementRepository repository)
        {
            _repository = repository;
        }

        public async Task<Configuration?> GetAsync()
        {
            var repoConfiguration = await _repository.GetItemAsync<MfaConfiguration>(m => m.Name == "Default");

            return repoConfiguration != null ? new Configuration { MfaTemplate = repoConfiguration.MfaTemplate, EnableMfa = repoConfiguration.EnableMfa, UserMfaType = repoConfiguration.UserMfaTypes } : new Configuration { MfaTemplate = new MfaTemplate(), UserMfaType = [] };
        }
    }
}
