using Blocks.Genesis;

namespace Cloud.DomainService.Requests
{
    public class UpdateApiEndpointConfigRequest : IProjectKey
    {
        public string ProjectKey { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public bool IsCaptchaRequired { get; set; }
        public bool IsMfaRequired { get; set; }
    }
}
