using Blocks.Genesis;

namespace Mail.DomainService.Template
{
    public class DeleteTemplateRequest : IProjectKey
    {
        public string ItemId { get; set; }
        public string ProjectKey { get; set; }
    }
}
