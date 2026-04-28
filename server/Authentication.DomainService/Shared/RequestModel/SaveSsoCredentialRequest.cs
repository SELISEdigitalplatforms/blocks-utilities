using Blocks.Genesis;

namespace DomainService.Shared
{
    public class SaveSsoCredentialRequest : IProjectKey
    {
        public string Provider { get; set; }
        public string Audience { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string RedirectUrl { get; set; }
        public string? WellKnownUrl { get; set; }
        public List<string> InitialRoles { get; set; } = [];
        public List<string> InitialPermissions { get; set; } = [];
        public string? ProjectKey { get; set; }
        public bool IsDisabled { get; set; }
        public string? ItemId { get; set; }
        public SSOType SSOType { get; set; }
        public string? TeamId { get; set; }
        public string? KeyId { get; set; }
        public string? PrivateKey { get; set; }
    }
}
