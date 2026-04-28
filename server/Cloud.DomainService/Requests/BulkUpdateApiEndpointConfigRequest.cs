using Blocks.Genesis;

namespace Cloud.DomainService.Requests
{
    public class BulkUpdateApiEndpointConfigRequest : IProjectKey
    {
        public string ProjectKey { get; set; } = string.Empty;
        public List<string> ItemIds { get; set; } = [];
        public bool IsCaptchaRequired { get; set; }
        public bool IsMfaRequired { get; set; }
        public bool DisableAll { get; set; }
    }
}
