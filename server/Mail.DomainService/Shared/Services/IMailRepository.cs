using Mail.DomainService.Entities;
using Mail.DomainService.Mails;

namespace Mail.DomainService.Services
{
    public interface IMailRepository
    {
        Task<bool> FileExists(string fileId);
        Task<List<string>> GetEmailAdressOfUsers(IEnumerable<string> emails);
        Task<MailServerConfiguration> GetMailServerConfigurationByTenantId(string tenantId);
        Task<EmailTemplate> GetEmailTemplateByPurpose(string purpose, string language, string organizationId);
        Task<MailServerConfiguration> GetMailServerConfigurationByPurpose(string purpose, string language, string organizationId);
        Task<bool> MailTemplateForPurposeExists(string purpose, string language);
        Task<bool> MailServerConfigurationExists(string purpose, string language);
        Task<bool> SaveMailToBeSent(MailToBeSent mailToBeSent);
        Task<MailToBeSent> GetMailToBeSent(string itemId);
        Task<(List<MailBoxEntity> Mails, long TotalCount)> GetMailBoxMails(GetMailBoxMails request); //deprecated
        Task<(List<MailBoxEntityResponse> Mails, long TotalCount)> GetMailBoxAggregatedMails(GetMailBoxMails request);
        Task<MailBoxEntity> GetMailBoxMail(string messageId, string projectKey);
    }
}
