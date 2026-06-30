namespace Mail.DomainService.Shared.Enums
{
    public enum MailStatus
    {
        Sent,
        Delivered,
        Failed,
        Pending,
        Quarantined,
        Bounced,
        Complained,
        Rejected,
        Received,
        Unknown
    }
}
