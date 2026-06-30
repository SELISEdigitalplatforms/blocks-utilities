using Mail.DomainService.Entities;
using Microsoft.Graph;

namespace Mail.DomainService.Mails
{
    public interface IMicrosoftGraphClientFactory
    {
        GraphServiceClient Create(MailServerConfiguration configuration);
    }
}
