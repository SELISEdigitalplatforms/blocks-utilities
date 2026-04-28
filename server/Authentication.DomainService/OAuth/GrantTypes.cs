namespace DomainService.OAuth
{
    public static class GrantTypes
    {
        public const string RefreshToken = "refresh_token";
        public const string Password = "password";
        public const string MfaCode = "mfa_code";
        public const string Social = "social";
        public const string AuthCode = "authorization_code";
        public const string BiometricAuthorization = "biometric_authorization";
        public const string ClientCredential = "client_credential";
        public const string ClientUserCode = "client_user_code";
        public const string SwitchOrganization = "switch_organization";
        public const string SsoConsentCode = "sso_consent";
    }
}
