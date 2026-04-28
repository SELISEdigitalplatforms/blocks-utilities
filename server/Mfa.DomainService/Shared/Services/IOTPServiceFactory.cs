using Iam.DomainService.Entities;

namespace Mfa.DomainService.Services
{
    public interface IOtpServiceFactory
    {
        IOtpService GetOTPService(UserMfaType authType);
    }
}
