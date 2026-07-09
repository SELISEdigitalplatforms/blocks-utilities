using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails.Services.DeliveryTracking
{
    public class ExchangeMessageTraceResult
    {
        public string Recipient { get; set; } = string.Empty;
        public MailStatus Status { get; set; } = MailStatus.Unknown;
        public string? StatusReason { get; set; }
    }
}
