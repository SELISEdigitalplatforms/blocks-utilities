using Blocks.Genesis;

namespace Mail.DomainService.Mails
{
    public interface IMailService
    {
        Task<BaseMutationResponse> ProcessMailToAnyAsync(SendMailToAny request);
        Task<BaseMutationResponse> ProcessMailAsync(SendMail request);
        Task<GetMailBoxMailsResponse> GetMailBoxMailsAsync(GetMailBoxMails request);
        Task<GetMailBoxMailResponse> GetMailBoxMailAsync(GetMailBoxMail request);
    }
}
