using Mail.DomainService.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails
{
    public class SmtpClientProvider
    {
        private readonly IServiceProvider serviceProvider;
        private readonly ILogger<SmtpClientProvider> logger;

        public SmtpClientProvider(IServiceProvider serviceProvider, ILogger<SmtpClientProvider> logger)
        {
            this.logger = logger;
            this.serviceProvider = serviceProvider;
        }

        public ISmtpClient GetSmtpClient(MailToBeSent mailToBeSent)
        {
            if (mailToBeSent.MailServerConfiguration.SmtpClient == SmtpClient.MsMailKit)
            {
                logger.LogInformation("Sending using .Net SMTP client");

                return serviceProvider.GetService<MicrosoftSmtpClient>();
            }
            else
            {
                logger.LogInformation("Sending using MailKit SMTP client");

                return serviceProvider.GetService<MailKitSmtpClient>();
            }
        }
    }
}
