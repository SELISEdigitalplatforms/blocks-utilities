using Blocks.Genesis;
using CloudConfiguration.DomainService.Authentication.Entities;
using CloudConfiguration.DomainService.Captcha.Entities;
using CloudConfiguration.DomainService.Captcha.RequestModel;
using CloudConfiguration.DomainService.Captcha.ResponseModel;
using CloudConfiguration.DomainService.IAM.Entities;
using CloudConfiguration.DomainService.MFA.Entities;
using CloudConfiguration.DomainService.Notification.Entities;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Notification.ResponseModel;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Mail.Entities;
using CloudConfiguration.DomainService.Mail.RequestModel;
using System.Linq.Expressions;

namespace CloudConfiguration.DomainService.Shared.Services
{
    public interface IConfigurationRepository
    {
        #region Authentication
        Task<AuthenticationConfiguration> GetAuthenticationConfigurationAsync();
        Task UpdateAuthenticationConfigAsync(AuthenticationConfiguration configuration);
        #endregion

        #region Captcha
        Task SaveCaptchaConfigurationAsync(CaptchaConfiguration repoConfiguration);
        Task UpdateCaptchaConfigurationStatusAsync(UpdateCaptchaConfigurationStatusRequest request);
        Task<CaptchaConfiguration> GetCaptchaConfigurationByIdAsync(string configurationId);
        Task<CaptchaConfiguration> GetCaptchaConfigurationByProviderAsync(string provider);
        Task<GetCaptchaConfigurationsResponse> GetCaptchaConfigurationsAsync(GetCaptchaConfigurationsRequest request);
        Task<CaptchaConfiguration> GetCaptchaConfigurationAsync();
        #endregion

        #region IAM

        Task SaveIamConfigurationAsync(IamConfiguration iamConfiguration);
        Task<IamConfiguration> GetIamConfigurationAsync();

        #endregion

        #region MFA

        Task<MfaConfiguration> GetDefaultMfaConfiguration();

        #endregion

        #region Notification

        Task SaveNotificationConfigurationAsync(NotificationConfiguration configuration);
        Task<NotificationConfiguration> GetNotificationConfigurationByIdAsync(string id);
        Task<GetNotificationConfigurationsResponse> GetNotificationConfigurationsAsync(GetNotificationConfigurationsRequest request);
        Task<BaseResponse> DeleteNotificationConfigurationAsync(DeleteNotificatoinConfigurationRequest request);
        Task<NotificationConfiguration> GetNotificationConfigurationByNameAsync(string name);

        #endregion

        #region Storage

        public Task SaveStorageConfigurationAsync(StorageConfiguration configuration);
        Task<StorageConfiguration> GetStorageConfigurationByNameAsync(string configurationName);
        Task<List<StorageConfiguration>> GetAllStorageConfigurationsByDateAsync();
        Task DeleteStorageConfigurationByNameAsync(string configurationName);
        Task<StorageConfiguration> GetStorageConfigurationByIdAsync(string itemId);
        Task<StorageConfiguration?> GetStorageConfigurationStrategyAsync(string storageStrategy);

        #endregion

        #region Mail

        Task SaveMailConfigurationAsync(MailServerConfiguration configuration);
        Task<MailServerConfiguration> GetMailConfigurationByIdAsync(string configurationId);
        Task<MailConfiguration> GetMailConfigurationByNameAsync(string configurationName);
        Task<List<MailServerConfiguration>> GetAllMailConfigurationsAsync();
        Task DeleteMailConfigurationAsync(string configurationId);

        #endregion

        Task UpsertAsync<T>(T data, Expression<Func<T, bool>> filterExpression, string collectionName = "");
    }
}
