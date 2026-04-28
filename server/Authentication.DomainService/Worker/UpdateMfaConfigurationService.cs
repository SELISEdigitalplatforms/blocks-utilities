using Blocks.Genesis;
using DomainService.Services;
using Mfa.DomainService.Configuration;

namespace DomainService.Worker
{
    public class UpdateMfaConfigurationService : IConsumer<MfaActionEvent>
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private const string MfaGrantType = "mfa_code";

        public UpdateMfaConfigurationService(IAuthenticationRepository authenticationRepository)
        {
            _authenticationRepository = authenticationRepository;
        }

        public async Task Consume(MfaActionEvent context)
        {
            var config = await _authenticationRepository.GetAuthenticationConfigurationAsync();

            if (context.IsEnable && !config.AllowedGrantTypes.Contains(MfaGrantType))
                config.AllowedGrantTypes.Add(MfaGrantType);

            else if (!context.IsEnable)
                config.AllowedGrantTypes.Remove(MfaGrantType);

            await _authenticationRepository.UpdateAuthenticationConfigurationAsync(config);
        }
    }
}
