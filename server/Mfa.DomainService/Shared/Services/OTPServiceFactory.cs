using Iam.DomainService.Entities;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.TOTP;
using Microsoft.Extensions.DependencyInjection;

namespace Mfa.DomainService.Services
{
    public class OtpServiceFactory : IOtpServiceFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public OtpServiceFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IOtpService GetOTPService(UserMfaType authType)
        {
            return authType switch
            {
                UserMfaType.TOTP => _serviceProvider.GetRequiredService<TotpService>(),
                UserMfaType.Email => _serviceProvider.GetRequiredService<EmailOtpService>(),

                _ => throw new ArgumentException("Invalid MfaAuthType", authType.ToString())
            };
        }
    }
}
