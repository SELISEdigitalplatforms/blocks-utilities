using Blocks.Genesis;

namespace Mail.DomainService.Mails
{
    public class SendMailToAny : BaseMailRequest
    {
        public bool? IsTestMail { get; set; }
    }
}
