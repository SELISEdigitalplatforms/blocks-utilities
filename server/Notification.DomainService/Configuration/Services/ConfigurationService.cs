using Blocks.Genesis;
using DomainService.Entities;
using FluentValidation;

namespace DomainService.Configuration.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly IConfigurationRepository _configurationRepository;
        private readonly IValidator<SaveConfigurationRequest> _configurationValidator;

        public ConfigurationService(IConfigurationRepository configurationRepository,
                                    IValidator<SaveConfigurationRequest> configurationValidator)
        {
            _configurationRepository = configurationRepository;
            _configurationValidator = configurationValidator;
        }

        public async Task<BaseResponse> SaveConfigurationAsync(SaveConfigurationRequest configuration)
        {
            var validationResult = await _configurationValidator.ValidateAsync(configuration);

            if (!validationResult.IsValid)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e=>e.ErrorMessage)
                };
            }

            var repoConfig = await MapAsync(configuration);
            await _configurationRepository.SaveAsync(repoConfig);
            return new BaseResponse { IsSuccess = true };
        }

        private async Task<NotificationConfiguration> MapAsync(SaveConfigurationRequest configuration)
        {
           var repoConfig = await _configurationRepository.GetByNameAsync(configuration.Name);

            repoConfig = repoConfig ?? new NotificationConfiguration { ItemId = Guid.NewGuid().ToString(), CreatedDate = DateTime.UtcNow, CreatedBy = BlocksContext.GetContext()?.UserId};

            repoConfig.LastUpdatedDate = DateTime.UtcNow;
            repoConfig.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            repoConfig.Name = configuration.Name;
            repoConfig.ChannelToNotify = configuration.ChannelToNotify;
            repoConfig.NotificationType = configuration.NotificationType;
            repoConfig.EnablePersistence = configuration.EnablePersistence; 
            repoConfig.NotifyMethod = configuration.NotifyMethod;

            return repoConfig;
        }

        public async Task<GetConfigurationsResponse> GetsAsync(GetConfigurationsRequest request)
        {
            return await _configurationRepository.GetConfigurationsAsync(request);
        }

        public async Task<NotificationConfiguration> GetAsync(GetConfigurationRequest request)
        {
            return await _configurationRepository.GetByIdAsync(request.ItemId);
        }

        public async Task<BaseResponse> DeleteAsync(DeleteConfigurationRequest request)
        {
            return await _configurationRepository.DeleteConfigurationAsync(request);
        }
    }
}
