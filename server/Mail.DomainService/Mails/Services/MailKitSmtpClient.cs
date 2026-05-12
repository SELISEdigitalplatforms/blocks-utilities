using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Mail.DomainService.Mails
{
    public interface IMailKitSmtpClient : IDisposable
    {
        Task ConnectAsync(string host, int port, bool useSsl);
        Task AuthenticateAsync(string userName, string password);
        Task SendAsync(MimeMessage message);
        Task DisconnectAsync(bool quit);
    }

    internal sealed class MailKitSmtpClientAdapter : IMailKitSmtpClient
    {
        private readonly MailKit.Net.Smtp.SmtpClient _client = new();

        public Task ConnectAsync(string host, int port, bool useSsl) => _client.ConnectAsync(host, port, useSsl);
        public Task AuthenticateAsync(string userName, string password) => _client.AuthenticateAsync(userName, password);
        public Task SendAsync(MimeMessage message) => _client.SendAsync(message);
        public Task DisconnectAsync(bool quit) => _client.DisconnectAsync(quit);
        public void Dispose() => _client.Dispose();
    }

    public class MailKitSmtpClient : ISmtpClient
    {
        private readonly ILogger<MailKitSmtpClient> _logger;
        private readonly IConfiguration _configuration;

        public MailKitSmtpClient(ILogger<MailKitSmtpClient> logger,
                                IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        protected virtual IMailKitSmtpClient CreateSmtpClient()
        {
            return new MailKitSmtpClientAdapter();
        }

        public async Task<bool> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody)
        {
            var message = new MimeMessage
            {
                Subject = mailBody.Subject,
            };

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = mailBody.Body
            };

            message.Body = bodyBuilder.ToMessageBody();
            message.From.Add(new MailboxAddress(mailToBeSent.MailServerConfiguration.SenderName, mailToBeSent.MailServerConfiguration.SenderAddress));

            foreach (var recipient in mailToBeSent.To)
            {
                message.To.Add(MailboxAddress.Parse(recipient));
            }

            if (mailToBeSent.Cc != null)
            {
                foreach (var cc in mailToBeSent.Cc)
                {
                    message.Cc.Add(MailboxAddress.Parse(cc));
                }
            }

            if (mailToBeSent.Bcc != null)
            {
                foreach (var bcc in mailToBeSent.Bcc)
                {
                    message.Bcc.Add(MailboxAddress.Parse(bcc));
                }
            }

            if (mailToBeSent.ReplyTo != null)
            {
                foreach (var replyTo in mailToBeSent.ReplyTo)
                {
                    message.ReplyTo.Add(MailboxAddress.Parse(replyTo));
                }
            }

            try
            {
                using var client = CreateSmtpClient();

                _logger.LogInformation("Attempting to connect to SMTP server: {Host}:{Port} ", mailToBeSent.MailServerConfiguration.Host, mailToBeSent.MailServerConfiguration.Port);

                await client.ConnectAsync(mailToBeSent.MailServerConfiguration.Host,
                    mailToBeSent.MailServerConfiguration.Port, mailToBeSent.MailServerConfiguration.EnableSSL);

                await client.AuthenticateAsync(mailToBeSent.MailServerConfiguration.SenderUserName,
                    mailToBeSent.MailServerConfiguration.AccountPassword);

                _logger.LogInformation("SMTP server authentication successful.");

                message.Headers.Add("X-SES-CONFIGURATION-SET", _configuration["SnsConfigurationName"]);
                message.Headers.Add("X-Tenant-Id", BlocksContext.GetContext()?.TenantId);
                message.Headers.Add("X-Mail-Body", mailBody.Body);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error sending email: {Message}", e.Message);
                return false;
            }
        }
    }
}
