using Blocks.Genesis;

namespace Mfa.DomainService.TOTP
{
    public class SetUpUserTotpResponse : BaseResponse
    {
        public string QrImageUrl { get; set; }
        public string QrCode { get; set; }
    }
}
