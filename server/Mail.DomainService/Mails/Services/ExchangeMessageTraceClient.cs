using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails
{
    public class ExchangeMessageTraceClient : IExchangeMessageTraceClient
    {
        private readonly ILogger<ExchangeMessageTraceClient> _logger;

        public ExchangeMessageTraceClient(ILogger<ExchangeMessageTraceClient> logger)
        {
            _logger = logger;
        }

        public Task<IReadOnlyList<ExchangeMessageTraceResult>> GetDeliveryStatusesAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default)
        {
            _logger.LogWarning(
                "Exchange message trace provider is not configured. Returning Unknown delivery status. ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}",
                mailToBeSent.ItemId,
                mailToBeSent.ProjectKey,
                mailToBeSent.TenantId);

            var results = GetRecipients(mailToBeSent)
                .Select(recipient => new ExchangeMessageTraceResult
                {
                    Recipient = recipient,
                    Status = MailStatus.Unknown,
                    StatusReason = "ExchangeMessageTraceProviderNotConfigured"
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<ExchangeMessageTraceResult>>(results);
        }

        private static IEnumerable<string> GetRecipients(MailToBeSent mailToBeSent)
        {
            return (mailToBeSent.To ?? Enumerable.Empty<string>())
                .Concat(mailToBeSent.Cc ?? Enumerable.Empty<string>())
                .Concat(mailToBeSent.Bcc ?? Enumerable.Empty<string>())
                .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }
}
