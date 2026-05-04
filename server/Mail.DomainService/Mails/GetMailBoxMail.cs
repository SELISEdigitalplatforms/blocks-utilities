using Blocks.Genesis;

namespace Mail.DomainService.Mails
{
    public class GetMailBoxMail : IProjectKey
    {
        public string ProjectKey { get; set; }
        public string MessageId { get; set; }
    }
}