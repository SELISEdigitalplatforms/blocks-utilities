using Blocks.Genesis;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Shared;

namespace Blocks.MfaDriver;

/// <summary>
/// 
/// </summary>
public interface IMfaDriverService
{
    /// <summary>
    /// Generate OTP
    /// </summary>
    /// <param name="request">The request containing the UserId and ProjectKey to generate OTP.</param>
    /// <returns>A response containing the ImageUri, TwoFactorId and IsSuccess status.</returns>
    Task<OtpGenerationResponse> GenerateOtpAsync(OtpGenerationRequest request);
    /// <summary>
    /// Verify OTP
    /// </summary>
    /// <param name="request">The request containing the VerificationCode, TwoFactorId, AuthType and ProjectKey to verify OTP.</param>
    /// <returns>A response containing the IsValid and IsSuccess status.</returns>
    Task<OtpVerificationResponse> VerifyOtpAsync(VerifyOtpRequest request);
}
