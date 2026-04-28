namespace Mfa.DomainService.Shared.RequestModel
{
    public class ResendOtpRequest
    {
        public string MfaId { get; set; }
        public string? SendPhoneNumberAsEmailDomain { get; set; }
    }
}
