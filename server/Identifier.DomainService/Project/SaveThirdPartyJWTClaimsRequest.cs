using Blocks.Genesis;

namespace DomainService.Projects
{
    public class SaveThirdPartyJWTClaimsRequest : IProjectKey
    {
        public string? ItemId { get; set; }
        public string UserId { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
        public string UserName { get; set; }
        public string Roles { get; set; }
        public string? ProjectKey { get ; set ; }
    }

    public class SaveThirdPartyJWTClaimsResponse : BaseResponse
    {
        public string ItemId { get; set; }
    }
}
