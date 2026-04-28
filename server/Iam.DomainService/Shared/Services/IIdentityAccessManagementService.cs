using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;

namespace Iam.DomainService.Services
{
    public interface IIdentityAccessManagementService
    {
        string HashPassword(string password);
        Task SendToQueueAsync<T>(string queue, T payload) where T : class;
        Task SendToTopicAsync<T>(string queue, T payload) where T : class;
        Task<bool> SendEmailAsync(SendMail sendMailCommand);
        Task<bool> SendActivationToEmailAsync(User user, string accountActivationUri, string emailPurpose, string projectKey);
        Task<bool> SendAccountActivationEmailAsync(User user, string mailPurpose, string projectKey);
        bool IsRoot();
    }
}
