using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Mails;

namespace Mail.Worker.Consumers
{
    public class MailOutboxProcessConsumer : IConsumer<ProcessMailOutboxMessageCommand>
    {
        private readonly IMailOutboxService _mailOutboxService;

        public MailOutboxProcessConsumer(IMailOutboxService mailOutboxService)
        {
            _mailOutboxService = mailOutboxService;
        }

        public async Task Consume(ProcessMailOutboxMessageCommand command)
        {
            var delay = command.NotBeforeUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            await _mailOutboxService.ProcessOutboxMessageAsync(command.TenantId, command.OutboxMessageId);
        }
    }
}
