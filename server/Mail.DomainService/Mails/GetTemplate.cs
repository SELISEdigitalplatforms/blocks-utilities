using Blocks.Genesis;

namespace Mail.DomainService.Mails
{
    public class GetTemplate : IProjectKey
    {
        public string ItemId { get; set; }
        public string ProjectKey { get; set; }
    }
}
