using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace DomainService.OAuth
{
    public class JwtAccessToken
    {
        public string Issuer { get; set; }
        public string Audience { get; set; }
        public IEnumerable<Claim> Claims { get; set; } = Enumerable.Empty<Claim>();
        public DateTime NotBefore { get; set; }
        public DateTime Expires { get; set; }
        public SigningCredentials SigningCredentials { get; set; }
        public int AccessTokenValidForNumberMinute { get; set; }
        public int RefreshTokenValidForNumberMinute { get; set; }
        public int RememberMeRefreshTokenValidForNumberMinute { get; set; }
    }
}
