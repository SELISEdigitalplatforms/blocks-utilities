using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;

namespace DomainService.OAuth.RequestModel
{
    public class TokenRequest
    {
        public string GrantType { get; set; }
        public string Code { get; set; }
        public string MfaId { get; set; }
        public UserMfaType MfaType { get; set; }
        public string RedirectUri { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Scope { get; set; }
        public bool RememberMe { get; set; }
        public HttpRequest Request { get; set; }
        public string RefreshToken { get; set; }
        public string State { get; set; }
        public string Language { get; set; }
        public string BiometricId { get; set; }
        public string BiometricKey { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string UserCode { get; set; }
        public string OrganizationId { get; set; }
    }
}
