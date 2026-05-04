using Blocks.Genesis;

namespace Mail.DomainService.Mails
{
    public class SendMailToAny : BaseMailRequest, IProjectKey
    {
        public bool? IsTestMail { get; set; }
        public string? ProjectKey { get; set; }
    }
}
