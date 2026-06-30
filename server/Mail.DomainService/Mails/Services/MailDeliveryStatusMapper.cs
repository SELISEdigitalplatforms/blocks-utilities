using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails
{
    public static class MailDeliveryStatusMapper
    {
        public static MailStatus Map(string? exchangeStatus)
        {
            return exchangeStatus?.Trim().ToLowerInvariant() switch
            {
                "delivered" => MailStatus.Delivered,
                "failed" => MailStatus.Failed,
                "pending" => MailStatus.Pending,
                "quarantined" => MailStatus.Quarantined,
                "filteredasspam" => MailStatus.Quarantined,
                "filtered as spam" => MailStatus.Quarantined,
                "rejected" => MailStatus.Rejected,
                "bounced" => MailStatus.Bounced,
                _ => MailStatus.Unknown
            };
        }
    }
}
