using Mail.DomainService.Entities;
using Microsoft.Graph;

namespace Mail.DomainService.Mails.Services.Transport
{
    public interface IMicrosoftGraphClientFactory
    {
        GraphServiceClient Create(MailServerConfiguration configuration);
    }
}
