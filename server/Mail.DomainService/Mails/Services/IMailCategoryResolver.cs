using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails
{
    public interface IMailCategoryResolver
    {
        Task<MailCategory> ResolveAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default);
    }
}
