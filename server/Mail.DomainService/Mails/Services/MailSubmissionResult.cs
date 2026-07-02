namespace Mail.DomainService.Mails
{
    public class MailSubmissionResult
    {
        public bool IsAccepted { get; set; }
        public bool IsRetryable { get; set; }
        public int? ProviderStatusCode { get; set; }
        public string? ProviderRequestId { get; set; }
        public string? FailureReason { get; set; }
        public int? RetryAfterSeconds { get; set; }
        public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;

        public static MailSubmissionResult Accepted(int? providerStatusCode = null, string? providerRequestId = null)
        {
            return new MailSubmissionResult
            {
                IsAccepted = true,
                IsRetryable = false,
                ProviderStatusCode = providerStatusCode,
                ProviderRequestId = providerRequestId,
                CompletedAtUtc = DateTime.UtcNow
            };
        }

        public static MailSubmissionResult Failed(
            string failureReason,
            bool isRetryable,
            int? providerStatusCode = null,
            string? providerRequestId = null,
            int? retryAfterSeconds = null)
        {
            return new MailSubmissionResult
            {
                IsAccepted = false,
                IsRetryable = isRetryable,
                ProviderStatusCode = providerStatusCode,
                ProviderRequestId = providerRequestId,
                FailureReason = failureReason,
                RetryAfterSeconds = retryAfterSeconds,
                CompletedAtUtc = DateTime.UtcNow
            };
        }
    }
}
