using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Blocks.Genesis;
using Mail.DomainService.Template.Services;
using Mail.DomainService.Template;
using Mail.DomainService.Template.Validators;

namespace Mail.DomainService.Shared.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void RegisterAllMailApplicationServices(this IServiceCollection services)
        {
           // services.AddTransient<IValidator<Configuration.Configuration>, ConfigurationValidator>();
            services.AddTransient<IValidator<Template.Template>, TemplateValidator>();

            // Register services
           // services.AddSingleton<IConfigurationService, ConfigurationService>();
           // services.AddSingleton<IConfigurationRepository, ConfigurationRepository>();
            services.AddSingleton<ITemplateService, TemplateService>();
            services.AddSingleton<ITemplateRepository, TemplateRepository>();



            services.AddSingleton<IMailRepository, MailRepository>();
            services.AddSingleton<SmtpClientProvider>();
            services.AddTransient<MailKitSmtpClient>();
            services.AddTransient<MicrosoftSmtpClient>();
            services.AddSingleton<ISendMailService, SendMailService>();
            services.AddSingleton<IMailService, MailService>();

            services.AddTransient<IValidator<MailToBeSent>, EmailValidator>();
            services.AddSingleton<CommonEmailValidator>();
        }
    }
}
