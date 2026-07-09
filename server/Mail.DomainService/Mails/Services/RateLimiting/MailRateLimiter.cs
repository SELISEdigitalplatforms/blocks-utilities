using Mail.DomainService.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails.Services.RateLimiting
{
    public class MailRateLimiter : IMailRateLimiter
    {
        private readonly IMailRateLimitCounterStore _counterStore;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MailRateLimiter> _logger;

        public MailRateLimiter(
            IMailRateLimitCounterStore counterStore,
            IConfiguration configuration,
            ILogger<MailRateLimiter> logger)
        {
            _counterStore = counterStore;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<MailRateLimitResult> CheckAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default)
        {
            if (!_configuration.GetValue("MailRateLimiting:Enabled", true))
            {
                return MailRateLimitResult.Allowed();
            }

            var cost = GetRecipientCost(mailToBeSent);
            var now = DateTime.UtcNow;
            var rules = BuildRules(mailToBeSent);

            foreach (var rule in rules)
            {
                cancellationToken.ThrowIfCancellationRequested();

                MailRateLimitCounterClaimResult result;
                try
                {
                    result = await TryClaimAsync(rule, cost, now, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var unavailableRetryAfterSeconds = Math.Max(
                        1,
                        _configuration.GetValue<int?>("MailRateLimiting:RedisFailureRetryAfterSeconds") ?? 30);
                    _logger.LogError(
                        ex,
                        "Redis mail rate limiter unavailable; request rejected. Scope={Scope}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                        rule.Scope,
                        mailToBeSent.ProjectKey,
                        mailToBeSent.TenantId,
                        mailToBeSent.OrganizationId);
                    return MailRateLimitResult.Rejected(rule.Scope, "MailRateLimiterUnavailable", unavailableRetryAfterSeconds);
                }
                if (result.IsAllowed)
                {
                    continue;
                }

                var retryAfterSeconds = GetRetryAfterSeconds(now, result.WindowEndUtc);
                _logger.LogWarning(
                    "Mail send request rate limited. Scope={Scope}, LimiterKey={LimiterKey}, Cost={Cost}, Used={Used}, Limit={Limit}, RetryAfterSeconds={RetryAfterSeconds}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}, SenderAddress={SenderAddress}",
                    rule.Scope,
                    rule.LimiterKey,
                    cost,
                    result.Used,
                    result.Limit,
                    retryAfterSeconds,
                    mailToBeSent.ProjectKey,
                    mailToBeSent.TenantId,
                    mailToBeSent.OrganizationId,
                    GetSenderAddress(mailToBeSent));

                return MailRateLimitResult.Rejected(rule.Scope, "MailRateLimitExceeded", retryAfterSeconds);
            }

            return MailRateLimitResult.Allowed();
        }

        private IEnumerable<RateLimitRule> BuildRules(MailToBeSent mailToBeSent)
        {
            var tenantId = NormalizeScopeValue(mailToBeSent.TenantId);
            var projectKey = NormalizeScopeValue(mailToBeSent.ProjectKey);
            var organizationId = NormalizeScopeValue(mailToBeSent.OrganizationId);
            var senderAddress = NormalizeScopeValue(GetSenderAddress(mailToBeSent));

            yield return CreateRule(
                "TenantMinute",
                $"mail-domain:tenant-minute:{tenantId}",
                TimeSpan.FromMinutes(1),
                "MailRateLimiting:DefaultPerTenantPerMinuteRecipientLimit",
                1000);

            yield return CreateRule(
                "TenantHour",
                $"mail-domain:tenant-hour:{tenantId}",
                TimeSpan.FromHours(1),
                "MailRateLimiting:DefaultPerTenantPerHourRecipientLimit",
                10000);

            yield return CreateRule(
                "ProjectMinute",
                $"mail-domain:project-minute:{tenantId}:{projectKey}:{organizationId}",
                TimeSpan.FromMinutes(1),
                "MailRateLimiting:DefaultPerProjectPerMinuteRecipientLimit",
                300);

            yield return CreateRule(
                "ProjectHour",
                $"mail-domain:project-hour:{tenantId}:{projectKey}:{organizationId}",
                TimeSpan.FromHours(1),
                "MailRateLimiting:DefaultPerProjectPerHourRecipientLimit",
                3000);

            yield return CreateRule(
                "SenderMinute",
                $"mail-domain:sender-minute:{tenantId}:{senderAddress}",
                TimeSpan.FromMinutes(1),
                "MailRateLimiting:DefaultPerSenderPerMinuteRecipientLimit",
                500);
        }

        private async Task<MailRateLimitCounterClaimResult> TryClaimAsync(
            RateLimitRule rule,
            int cost,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var windowStartUtc = GetWindowStart(now, rule.Window);
            return await _counterStore.TryClaimAsync(new MailRateLimitCounterClaim
            {
                LimiterKey = rule.LimiterKey,
                WindowStartUtc = windowStartUtc,
                WindowEndUtc = windowStartUtc.Add(rule.Window),
                Limit = rule.Limit,
                Cost = cost
            }, cancellationToken);
        }

        private RateLimitRule CreateRule(string scope, string limiterKey, TimeSpan window, string configKey, int defaultLimit)
        {
            var limit = Math.Max(1, _configuration.GetValue<int?>(configKey) ?? defaultLimit);
            return new RateLimitRule(scope, limiterKey, window, limit);
        }

        private static int GetRecipientCost(MailToBeSent mailToBeSent)
        {
            var recipients = (mailToBeSent.To ?? Enumerable.Empty<string>())
                .Concat(mailToBeSent.Cc ?? Enumerable.Empty<string>())
                .Concat(mailToBeSent.Bcc ?? Enumerable.Empty<string>())
                .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return Math.Max(1, recipients);
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
