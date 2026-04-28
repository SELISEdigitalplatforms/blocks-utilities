using Blocks.Genesis;
using Microsoft.AspNetCore.Mvc;
using CloudConfiguration.DomainService.Authentication;
using CloudConfiguration.DomainService.Captcha.RequestModel;
using CloudConfiguration.DomainService.Captcha.ResponseModel;
using CloudConfiguration.DomainService.IAM.RequestModel;
using CloudConfiguration.DomainService.IAM.ResponseModel;
using CloudConfiguration.DomainService.MFA.RequestModel;
using CloudConfiguration.DomainService.MFA.ResponseModel;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Notification.ResponseModel;
using CloudConfiguration.DomainService.Notification.Entities;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Storage.RequestModel;
using CloudConfiguration.DomainService.Mail.Entities;
using CloudConfiguration.DomainService.Mail.RequestModel;

namespace CloudConfiguration.DomainService.Shared.Services
{
    public interface IConfigurationService
    {
        #region Authentication
        Task<IActionResult> GetAuthenticationConfigAsync();
        Task<BaseResponse> UpdateAuthenticationConfigAsync(UpdateAuthenticationConfigurationRequest configuration);
        #endregion

        #region Captcha
        Task<BaseMutationResponse> SaveCaptchaConfigurationAsync(SaveCaptchaConfigurationRequest request);
        Task<BaseMutationResponse> UpdateCaptchaConfigurationStatusAsync(UpdateCaptchaConfigurationStatusRequest request);
        Task<GetCaptchaConfigurationResponse> GetCaptchaConfigurationAsync(string provider);
        Task<GetCaptchaConfigurationsResponse> GetCaptchaConfigurationsAsync(GetCaptchaConfigurationsRequest request);
        #endregion

        #region IAM
        Task<BaseMutationResponse> SaveIamConfigurationAsync(SaveIamConfigurationRequest request);
        Task<GetConfigurationResponse> GetIamConfigurationAsync();
        #endregion

        #region MFA

        Task<BaseResponse> SaveMfaConfigurationAsync(SaveMfaConfigurationRequest request);
        Task<GetMfaConfigurationResponse> GetMfaConfigurationAsync();

        #endregion

        #region Notification

        Task<BaseResponse> SaveNotificationConfigurationAsync(SaveNotificatonConfigurationRequest configuration);
        Task<GetNotificationConfigurationsResponse> GetNotificationConfigurationsAsync(GetNotificationConfigurationsRequest request);
        Task<NotificationConfiguration> GetNotificatoinConfigurationAsync(GetNotificationConfigurationRequest request);
        Task<BaseResponse> DeleteNotificationConfigurationAsync(DeleteNotificatoinConfigurationRequest request);

        #endregion

        #region Storage

        Task<BaseMutationResponse> SaveStorageConfigurationAsync(SaveStorageConfigurationRequest request);
        Task<List<StorageConfiguration>> GetStorageConfigurationsAsync();
        Task<StorageConfiguration> GetStorageConfigurationAsync(string configurationName);
        Task<BaseResponse> DeleteStorageConfigurationAsync(string configurationName);

        #endregion

        #region Mail

        Task<BaseMutationResponse> SaveMailConfigurationAsync(MailConfiguration configuration);
        Task<MailConfiguration> GetMailConfigurationAsync(GetMailConfigurationRequest request);
        Task<List<MailServerConfiguration>> GetAllMailConfigurationsAsync();
        Task<BaseMutationResponse> DeleteMailConfigurationAsync(DeleteMailConfigurationRequest request);
        Task<BaseMutationResponse> DuplicateMailConfigurationAsync(DuplicateMailConfigurationRequest request);

        #endregion
    }
}
