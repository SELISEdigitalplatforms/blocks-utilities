using Iam.DomainService.Entities;

namespace DomainService.OAuth.ResponseModel
{
    public class TokenResponse
    {
        public string AccessToken { get; set; }
        public int ExpiresIn { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public string RefreshToken { get; set; }
        public DateTime RefreshExpiresUtc { get; set; }
        public string Error { get; set; }
        public string ErrorDescription { get; set; } = string.Empty;
        public string CookieDomain { get; set; }
        public string MfaId { get; set; }
        public UserMfaType UserMfa { get; set; }
        public int StatusCode { get; set; }

        public string SsoUserRedirectUrl { get; set; }
    }
}
