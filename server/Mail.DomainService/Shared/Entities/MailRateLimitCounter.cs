namespace Mail.DomainService.Entities
{
    public class MailRateLimitCounter
    {
        public string ItemId { get; set; } = string.Empty;
        public string LimiterKey { get; set; } = string.Empty;
        public DateTime WindowStartUtc { get; set; }
        public DateTime WindowEndUtc { get; set; }
        public int Used { get; set; }
        public int Limit { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
