using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Mails;

namespace Mail.Worker.Consumers
{
    public abstract class SendEmailConsumerBase<TCommand> : IConsumer<TCommand>
        where TCommand : SendEmailCommand
    {
        private readonly ISendMailService _sendMailService;

        protected SendEmailConsumerBase(ISendMailService sendMailService)
        {
            _sendMailService = sendMailService;
        }

        public async Task Consume(TCommand sendEmailCommand)
        {
            await _sendMailService.ProcessSendMailAsync(sendEmailCommand);
        }
    }

    public class SendEmailConsumer : SendEmailConsumerBase<SendEmailCommand>
    {
        public SendEmailConsumer(ISendMailService sendMailService) : base(sendMailService)
        {
        }
    }

    public class NoAttachmentSendEmailConsumer : SendEmailConsumerBase<NoAttachmentSendEmailCommand>
    {
        public NoAttachmentSendEmailConsumer(ISendMailService sendMailService) : base(sendMailService)
        {
        }
    }

    public class SmallAttachmentSendEmailConsumer : SendEmailConsumerBase<SmallAttachmentSendEmailCommand>
    {
        public SmallAttachmentSendEmailConsumer(ISendMailService sendMailService) : base(sendMailService)
        {
        }
    }

    public class LargeAttachmentSendEmailConsumer : SendEmailConsumerBase<LargeAttachmentSendEmailCommand>
    {
        public LargeAttachmentSendEmailConsumer(ISendMailService sendMailService) : base(sendMailService)
        {
        }
    }
}
