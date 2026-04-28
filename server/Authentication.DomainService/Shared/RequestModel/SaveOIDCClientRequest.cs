
using Blocks.Genesis;

namespace DomainService.RequestModel
{
    public class SaveOIDCClientRequest : IProjectKey
    {
        public string RedirectUri { get; set; }
        public string Scope { get; set; }
        public string Audience { get; set; }
        public bool IsAutoRedirect { get; set; }
        public string? ItemId { get; set; }
        public string ProjectKey { get; set; }
        public string? ClientLogoUrl { get; set; }
        public string? ClientDisplayName { get; set; }
        public string? ClientBrandColor { get; set; }
    }
}
