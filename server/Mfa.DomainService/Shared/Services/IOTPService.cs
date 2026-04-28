using Mfa.DomainService.Entities;
using Mfa.DomainService.Shared;

namespace Mfa.DomainService.Services
{
    public interface IOtpService
    {
        Task<OtpGenerationResponse> GenerateAsync(UserInfo userInfo, string? sendPhoneNumberAsEmailDomain = null);
        Task<OtpVerificationResponse> VerifyAsync(VerifyOtpRequest request);
    }
}
