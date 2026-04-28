using Blocks.Genesis;

namespace Mfa.DomainService.TOTP
{
    public class UserTotpDetail : BaseEntity
    {
        public string ImageUri { get; set; }
        public string TowFactorId { get; set; }
        public string Secret { get; set; }
    }
}
