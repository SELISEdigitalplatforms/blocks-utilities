using Azure.Identity;
using Mail.DomainService.Entities;
using Microsoft.Graph;

namespace Mail.DomainService.Mails.Services.Transport
{
    public class MicrosoftGraphClientFactory : IMicrosoftGraphClientFactory
    {
        private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

        public GraphServiceClient Create(MailServerConfiguration configuration)
        {
            var credential = new ClientSecretCredential(
                configuration.TenantId,
                configuration.SenderUserName,
                configuration.AccountPassword);

            return new GraphServiceClient(credential, Scopes);
        }
    }
}
