using Blocks.Genesis;

namespace Mail.DomainService.Mails.Services.Core
{
    public interface IMailService
    {
        Task<BaseMutationResponse> ProcessMailToAnyAsync(SendMailToAny request);
        Task<BaseMutationResponse> ProcessMailAsync(SendMail request);
        Task<GetEmailSendsResponse> GetEmailSendsAsync(GetEmailSends request);
        Task<GetMailBoxMailsResponse> GetMailBoxMailsAsync(GetMailBoxMails request);
        Task<GetMailBoxMailResponse> GetMailBoxMailAsync(GetMailBoxMail request);
    }
}
