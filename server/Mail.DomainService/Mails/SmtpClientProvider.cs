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

        public virtual ISmtpClient GetSmtpClient(MailToBeSent mailToBeSent)
        {
            switch (mailToBeSent.MailServerConfiguration.SmtpClient)
            {
                case SmtpClient.MsGraph:
                    logger.LogInformation("Sending using Microsoft Graph mail client");
                    return serviceProvider.GetRequiredService<MicrosoftGraphServiceClient>();

                case SmtpClient.MsMailKit:
                    logger.LogInformation("Sending using MailKit SMTP client");
                    return serviceProvider.GetRequiredService<MailKitSmtpClient>();

                case SmtpClient.Default:
                default:
                    logger.LogInformation("Sending using default MailKit SMTP client");
                    return serviceProvider.GetRequiredService<MailKitSmtpClient>();
            }
        }
    }
}
