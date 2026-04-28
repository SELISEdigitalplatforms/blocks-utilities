using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using CloudConfiguration.DomainService.Captcha.RequestModel;
using CloudConfiguration.DomainService.Captcha.Validators;
using CloudConfiguration.DomainService.IAM.RequestModel;
using CloudConfiguration.DomainService.IAM.Validators;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Notification.Validators;
using CloudConfiguration.DomainService.Storage.RequestModel;
using CloudConfiguration.DomainService.Storage.Validators;
using CloudConfiguration.DomainService.Mail.RequestModel;
using CloudConfiguration.DomainService.Mail.Validators;

namespace CloudConfiguration.DomainService.Shared.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void AddCloudConfigurationServices(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<IConfigurationService, ConfigurationService>();
            serviceCollection.AddSingleton<IConfigurationRepository, ConfigurationRepository>();

            serviceCollection.AddSingleton<IValidator<SaveCaptchaConfigurationRequest>, CaptchaConfigurationValidator>();
            serviceCollection.AddSingleton<IValidator<SaveIamConfigurationRequest>, SaveIamConfigurationValidator>();
            serviceCollection.AddSingleton<IValidator<SaveNotificatonConfigurationRequest>, NotificationConfigurationValidator>();
            serviceCollection.AddSingleton<IValidator<SaveStorageConfigurationRequest>, StorageConfigurationValidator>();
            serviceCollection.AddSingleton<IValidator<MailConfiguration>, MailConfigurationValidator>();
        }
    }
}
