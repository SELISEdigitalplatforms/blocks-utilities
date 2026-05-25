using Blocks.Genesis;
using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails
{
    public class GetMailBoxMailResponse : BaseResponse
    {
        public MailBoxEntity Mail { get; set; }
    }
}