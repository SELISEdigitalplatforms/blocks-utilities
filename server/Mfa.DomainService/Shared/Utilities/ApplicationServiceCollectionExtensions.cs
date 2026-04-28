using Blocks.Extension.DependencyInjection;
using Blocks.Genesis;
using FluentValidation;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Mfa.DomainService.Validators;
using Microsoft.Extensions.DependencyInjection;

namespace Mfa.DomainService.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void RegisterAllServices(this IServiceCollection serviceCollection)
        {
            #region Services
            serviceCollection.AddSingleton<IMfaManagementService, MfaManagementService>();
            serviceCollection.AddSingleton<IOtpServiceFactory, OtpServiceFactory>();
            serviceCollection.AddSingleton<IMfaManagementRepository, MfaManagementRepository>();
            serviceCollection.AddSingleton<IMfaConfigurationService, MfaConfigurationService>();
            serviceCollection.AddSingleton<TotpService>();
            serviceCollection.AddSingleton<EmailOtpService>();
            serviceCollection.AddSingleton<ChangeControllerContext>();
            serviceCollection.AddHttpContextAccessor();
            #endregion

            #region Validators
            serviceCollection.AddTransient<IValidator<VerifyOtpRequest>, VerifyOtpRequestValidator>();
            #endregion

            serviceCollection.RegisterBlocksMailService();
        }
    }
}
