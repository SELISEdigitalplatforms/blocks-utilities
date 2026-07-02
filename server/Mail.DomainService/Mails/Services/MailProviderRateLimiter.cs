using Mail.DomainService.Entities;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails
{
    public class MailProviderRateLimiter : IMailProviderRateLimiter
    {
        private readonly IMailRepository _mailRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MailProviderRateLimiter> _logger;

        public MailProviderRateLimiter(
            IMailRepository mailRepository,
            IConfiguration configuration,
            ILogger<MailProviderRateLimiter> logger)
        {
            _mailRepository = mailRepository;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<MailRateLimitResult> CheckAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default)
        {
            if (!_configuration.GetValue("MicrosoftGraphProviderRateLimiting:Enabled", true))
            {
                return MailRateLimitResult.Allowed();
            }

            var now = DateTime.UtcNow;
            foreach (var rule in BuildRules(mailToBeSent))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await _mailRepository.TryIncrementRateLimitCounterAsync(new MailRateLimitCounterClaim
                {
                    LimiterKey = rule.LimiterKey,
                    WindowStartUtc = GetWindowStart(now, rule.Window),
                    WindowEndUtc = GetWindowStart(now, rule.Window).Add(rule.Window),
                    Limit = rule.Limit,
                    Cost = 1
                });

                if (result.IsAllowed)
                {
                    continue;
                }

                var retryAfterSeconds = Math.Max(
                    GetRetryAfterSeconds(now, result.WindowEndUtc),
                    Math.Max(1, _configuration.GetValue<int?>("MicrosoftGraphProviderRateLimiting:DefaultRetryAfterSeconds") ?? 60));

                _logger.LogWarning(
                    "Microsoft Graph provider rate limited mail submission. Scope={Scope}, LimiterKey={LimiterKey}, Used={Used}, Limit={Limit}, RetryAfterSeconds={RetryAfterSeconds}, ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}, SenderAddress={SenderAddress}",
                    rule.Scope,
                    rule.LimiterKey,
                    result.Used,
                    result.Limit,
                    retryAfterSeconds,
                    mailToBeSent.ItemId,
                    mailToBeSent.ProjectKey,
                    mailToBeSent.TenantId,
                    mailToBeSent.OrganizationId,
                    GetSenderAddress(mailToBeSent));

                return MailRateLimitResult.Rejected(rule.Scope, "MicrosoftGraphProviderRateLimitExceeded", retryAfterSeconds);
            }

            return MailRateLimitResult.Allowed();
        }

        private IEnumerable<RateLimitRule> BuildRules(MailToBeSent mailToBeSent)
        {
            var tenantId = NormalizeScopeValue(mailToBeSent.TenantId);
            var graphClientId = NormalizeScopeValue(mailToBeSent.MailServerConfiguration?.SenderUserName);
            var senderAddress = NormalizeScopeValue(GetSenderAddress(mailToBeSent));

            yield return CreateRule(
                "ProviderTenantMinute",
                $"mail-provider:tenant-minute:{tenantId}",
                "MicrosoftGraphProviderRateLimiting:PerTenantPerMinuteSubmissionLimit",
                600);

            yield return CreateRule(
                "ProviderClientMinute",
                $"mail-provider:client-minute:{tenantId}:{graphClientId}",
                "MicrosoftGraphProviderRateLimiting:PerClientPerMinuteSubmissionLimit",
                300);

            yield return CreateRule(
                "ProviderSenderMinute",
                $"mail-provider:sender-minute:{tenantId}:{senderAddress}",
                "MicrosoftGraphProviderRateLimiting:PerSenderPerMinuteSubmissionLimit",
                120);

            if (mailToBeSent.MailCategory == MailCategory.LargeAttachment)
            {
                yield return CreateRule(
                    "ProviderLargeAttachmentSenderMinute",
                    $"mail-provider:large-sender-minute:{tenantId}:{senderAddress}",
                    "MicrosoftGraphProviderRateLimiting:LargeAttachmentPerSenderPerMinuteSubmissionLimit",
                    30);
            }
        }

        private RateLimitRule CreateRule(string scope, string limiterKey, string configKey, int defaultLimit)
        {
            var limit = Math.Max(1, _configuration.GetValue<int?>(configKey) ?? defaultLimit);
            return new RateLimitRule(scope, limiterKey, TimeSpan.FromMinutes(1), limit);
        }

        private static string GetSenderAddress(MailToBeSent mailToBeSent)
        {
            return !string.IsNullOrWhiteSpace(mailToBeSent.SenderAddress)
                ? mailToBeSent.SenderAddress
                : mailToBeSent.MailServerConfiguration?.SenderAddress ?? "unknown";
        }

        private static DateTime GetWindowStart(DateTime now, TimeSpan window)
        {
            var ticks = now.Ticks - (now.Ticks % window.Ticks);
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        private static int GetRetryAfterSeconds(DateTime now, DateTime windowEndUtc)
        {
            return Math.Max(1, (int)Math.Ceiling((windowEndUtc - now).TotalSeconds));
        }

        private static string NormalizeScopeValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            return value.Trim().ToLowerInvariant().Replace('|', '_');
        }

        private sealed record RateLimitRule(string Scope, string LimiterKey, TimeSpan Window, int Limit);
    }
}
