namespace Mail.DomainService.Mails
{
    public class MailRateLimitCounterClaimResult
    {
        public bool IsAllowed { get; set; }
        public int Used { get; set; }
        public int Limit { get; set; }
        public DateTime WindowEndUtc { get; set; }
    }
}
