namespace Mail.DomainService.Mails
{
    public class MailRateLimitResult
    {
        public bool IsAllowed { get; set; }
        public int RetryAfterSeconds { get; set; }
        public string Scope { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;

        public static MailRateLimitResult Allowed()
        {
            return new MailRateLimitResult
            {
                IsAllowed = true
            };
        }

        public static MailRateLimitResult Rejected(string scope, string reason, int retryAfterSeconds)
        {
            return new MailRateLimitResult
            {
                IsAllowed = false,
                Scope = scope,
                Reason = reason,
                RetryAfterSeconds = Math.Max(1, retryAfterSeconds)
            };
        }
    }
}
