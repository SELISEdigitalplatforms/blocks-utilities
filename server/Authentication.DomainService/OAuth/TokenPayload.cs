using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Mvc;

namespace DomainService.OAuth
{
    public class TokenPayload
    {
        [FromForm(Name = "grant_type")]
        public string GrantType { get; set; }

        [FromForm(Name = "code")]
        public string Code { get; set; } = string.Empty;

        [FromForm(Name = "redirect_uri")]
        public string RedirectUri { get; set; } = string.Empty;

        [FromForm(Name = "username")]
        public string Username { get; set; } = string.Empty;

        [FromForm(Name = "password")]
        public string Password { get; set; } = string.Empty;

        [FromForm(Name = "scope")]
        public string Scope { get; set; } = string.Empty;

        [FromForm(Name = "remember_me")]
        public bool RememberMe { get; set; }

        [FromForm(Name = "refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [FromForm(Name = "mfa_id")]
        public string MfaId { get; set; } = string.Empty;

        [FromForm(Name = "mfa_type")]
        public UserMfaType MfaType { get; set; }

        [FromForm(Name = "state")]
        public string State { get; set; } = string.Empty;

        [FromForm(Name = "language")]
        public string Language { get; set; } = string.Empty;

        [FromForm(Name = "biometric_id")]
        public string BiometricId { get; set; } = string.Empty;

        [FromForm(Name = "biometric_key")]
        public string BiometriKey { get; set; } = string.Empty;

        [FromForm(Name = "client_id")]
        public string ClientId { get; set; } = string.Empty;

        [FromForm(Name = "client_secret")]
        public string ClientSecret { get; set; } = string.Empty;
        [FromForm(Name = "user_code")]
        public string UserSecret { get; set; } = string.Empty;

        [FromForm(Name = "org_id")]
        public string OrganizationId { get; set; } = string.Empty;
    }
}
