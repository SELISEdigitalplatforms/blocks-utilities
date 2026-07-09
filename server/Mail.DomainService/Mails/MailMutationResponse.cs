using Blocks.Genesis;

namespace Mail.DomainService.Mails
{
    public class MailMutationResponse : BaseMutationResponse
    {
        public bool IsRateLimited { get; set; }
        public int? RetryAfterSeconds { get; set; }
        public string? RateLimitScope { get; set; }
    }
}
