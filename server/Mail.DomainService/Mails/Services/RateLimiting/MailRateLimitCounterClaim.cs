namespace Mail.DomainService.Mails.Services.RateLimiting
{
    public class MailRateLimitCounterClaim
    {
        public string LimiterKey { get; set; } = string.Empty;
        public DateTime WindowStartUtc { get; set; }
        public DateTime WindowEndUtc { get; set; }
        public int Limit { get; set; }
        public int Cost { get; set; }
    }
}
