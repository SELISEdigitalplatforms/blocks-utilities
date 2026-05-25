using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace Mail.DomainService.Mails
{
    public class SendMailService : ISendMailService
    {
        private readonly ILogger<SendMailService> _logger;
        private readonly IMailRepository _mailRepository;
        private readonly SmtpClientProvider _smtpClientProvider;

        public SendMailService(
            ILogger<SendMailService> logger,
            IMailRepository mailRepository,
            SmtpClientProvider smtpClientProvider
        )
        {
            _logger = logger;
            _mailRepository = mailRepository;
            _smtpClientProvider = smtpClientProvider;
        }
        public async Task ProcessSendMailAsync(SendEmailEvent sendEmailEvent)
        {
            var mailToBeSent = await _mailRepository.GetMailToBeSent(sendEmailEvent.ItemId);
            var smtpClient = _smtpClientProvider.GetSmtpClient(mailToBeSent);
            var mailBody = BuildMailBody(mailToBeSent);

            var success = await smtpClient.SendAsync(mailToBeSent, mailBody);
            var recipients = "HIDDEN recipients (" + string.Join(", ", mailToBeSent.To.Select(x => "*****@" + x.Split("@").LastOrDefault())) + ")";

            if (success)
            {
                var logMessage = string.Format("SUCCESS:\nTo: {0}\nSubject: {1}\nTime: {2}\nTemplate Name: {3}", recipients, mailToBeSent.EmailTemplate.TemplateSubject, DateTime.Now, mailToBeSent.EmailTemplate.Name);
                _logger.LogInformation("{LogMessage}", logMessage);
            }
            else
            {
                var logMessage = string.Format("FAILED:\nTo: {0}\nSubject: {1}\nTime: {2}\nTemplate Name: {3}", recipients, mailToBeSent.EmailTemplate.TemplateSubject, DateTime.Now, mailToBeSent.EmailTemplate.Name);

                _logger.LogError("{LogMessage}", logMessage);
            }
        }

        public MailBody BuildMailBody(MailToBeSent mailToBeSent)
        {
            return new MailBody
            {
                Subject = BuildSubject(mailToBeSent.EmailTemplate.TemplateSubject, mailToBeSent.SubjectDataContext),
                Body = BuildBody(mailToBeSent.EmailTemplate.TemplateBody, mailToBeSent.BodyDataContext)
            };
        }

        public static string BuildBody(string templateBody, Dictionary<string, string> placeHolderValues)
        {
            var body = templateBody;

            foreach (var placeHolderValue in placeHolderValues)
            {
                body = body.Replace("{{" + placeHolderValue.Key + "}}", WebUtility.HtmlEncode(placeHolderValue.Value));
            }

            return body;
        }

        public static string BuildSubject(string templateSubject, Dictionary<string, string> placeHolderValues)
        {
            var body = templateSubject;

            foreach (var placeHolderValue in placeHolderValues)
            {
                body = body.Replace("{{" + placeHolderValue.Key + "}}", placeHolderValue.Value);
            }

            return body;
        }
    }
}
