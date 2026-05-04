using Blocks.Genesis;

namespace Mail.DomainService.Mails
{
    public class SendMail : BaseMailRequest, IProjectKey
    {
        public string? ProjectKey { get; set; }
    }
}
