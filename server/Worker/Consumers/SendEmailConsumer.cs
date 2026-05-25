using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Mails;

namespace Mail.Worker.Consumers
{
    public class SendEmailConsumer : IConsumer<SendEmailEvent>
    {
        private readonly ISendMailService _sendMailService;

        public SendEmailConsumer(ISendMailService sendMailService)
        {
            _sendMailService = sendMailService;
        }

        public async Task Consume(SendEmailEvent sendEmailEvent)
        {
            await _sendMailService.ProcessSendMailAsync(sendEmailEvent);
        }
    }
}
