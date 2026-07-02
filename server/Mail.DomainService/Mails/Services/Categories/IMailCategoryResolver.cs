using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails.Services.Categories
{
    public interface IMailCategoryResolver
    {
        Task<MailCategory> ResolveAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default);
    }
}
