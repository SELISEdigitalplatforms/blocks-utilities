using Blocks.Genesis;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;

namespace Blocks.MfaDriver;

public class MfaDriverService : IMfaDriverService
{
    public readonly IMfaManagementService _mfaManagementService;

    public MfaDriverService(IMfaManagementService mfaManagementService)
    {
        _mfaManagementService = mfaManagementService;
    }
    public async Task<OtpGenerationResponse> GenerateOtpAsync(OtpGenerationRequest request)
    {
        return await _mfaManagementService.GenerateOTPAsync(request);
    }

    public async Task<OtpVerificationResponse> VerifyOtpAsync(VerifyOtpRequest request)
    {
        return await _mfaManagementService.VerifyOTPAsync(request);
    }
}
