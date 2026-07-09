using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Mails;

namespace Mail.Worker.Consumers
{
    public class MailDeliveryStatusConsumer : IConsumer<CheckMailDeliveryStatusCommand>
    {
        private readonly IMailDeliveryStatusService _mailDeliveryStatusService;

        public MailDeliveryStatusConsumer(IMailDeliveryStatusService mailDeliveryStatusService)
        {
            _mailDeliveryStatusService = mailDeliveryStatusService;
        }

        public async Task Consume(CheckMailDeliveryStatusCommand command)
        {
            var delay = command.NotBeforeUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            await _mailDeliveryStatusService.ProcessDeliveryStatusCheckAsync(command);
        }
    }
}
