using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Mail
{
    public class SmtpClientProviderTests
    {
        [Fact]
        public void GetSmtpClient_ReturnsGraphClient_WhenConfiguredForMsGraph()
        {
            var provider = CreateProvider();
            var mail = CreateMailToBeSent(SmtpClient.MsGraph);

            var result = provider.GetSmtpClient(mail);

            Assert.IsType<MicrosoftGraphServiceClient>(result);
        }

        [Fact]
        public void GetSmtpClient_ReturnsMailKitClient_WhenConfiguredForMsMailKit()
        {
            var provider = CreateProvider();
            var mail = CreateMailToBeSent(SmtpClient.MsMailKit);

            var result = provider.GetSmtpClient(mail);

            Assert.IsType<MailKitSmtpClient>(result);
        }

        [Fact]
        public void GetSmtpClient_PreservesDefaultMailKitBehavior()
        {
            var provider = CreateProvider();
            var mail = CreateMailToBeSent(SmtpClient.Default);

            var result = provider.GetSmtpClient(mail);

            Assert.IsType<MailKitSmtpClient>(result);
        }

        private static SmtpClientProvider CreateProvider()
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder().Build();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddTransient(_ => new MicrosoftGraphServiceClient(
                Mock.Of<IMicrosoftGraphClientFactory>(),
                Mock.Of<IMailAttachmentProvider>(),
                NullLogger<MicrosoftGraphServiceClient>.Instance));
            services.AddTransient(_ => new MailKitSmtpClient(
                NullLogger<MailKitSmtpClient>.Instance,
                configuration));

            return new SmtpClientProvider(services.BuildServiceProvider(), NullLogger<SmtpClientProvider>.Instance);
        }

        private static MailToBeSent CreateMailToBeSent(SmtpClient smtpClient)
        {
            return new MailToBeSent
            {
                MailServerConfiguration = new MailServerConfiguration
                {
                    SmtpClient = smtpClient
                }
            };
        }
    }
}
