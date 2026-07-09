using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails.Services.RateLimiting
{
    public class MailProviderRateLimiter : IMailProviderRateLimiter
    {
        private readonly IMailRateLimitCounterStore _counterStore;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MailProviderRateLimiter> _logger;

        public MailProviderRateLimiter(
            IMailRateLimitCounterStore counterStore,
            IConfiguration configuration,
            ILogger<MailProviderRateLimiter> logger)
        {
            _counterStore = counterStore;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<MailRateLimitResult> CheckAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default)
        {
            var provider = ResolveProvider(mailToBeSent);
            var providerConfigPath = $"MailProviderRateLimiting:{provider.ConfigSection}";
            if (!_configuration.GetValue("MailProviderRateLimiting:Enabled", true) ||
                !_configuration.GetValue($"{providerConfigPath}:Enabled", true))
            {
                return MailRateLimitResult.Allowed();
            }

            var now = DateTime.UtcNow;
            foreach (var rule in BuildRules(mailToBeSent, provider, providerConfigPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                MailRateLimitCounterClaimResult result;
                try
                {
                    var windowStartUtc = GetWindowStart(now, rule.Window);
                    result = await _counterStore.TryClaimAsync(new MailRateLimitCounterClaim
                    {
                        LimiterKey = rule.LimiterKey,
                        WindowStartUtc = windowStartUtc,
                        WindowEndUtc = windowStartUtc.Add(rule.Window),
                        Limit = rule.Limit,
                        Cost = 1
                    }, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var unavailableRetryAfterSeconds = Math.Max(
                        1,
                        _configuration.GetValue<int?>("MailProviderRateLimiting:RedisFailureRetryAfterSeconds") ?? 60);
                    _logger.LogError(
                        ex,
                        "Redis provider rate limiter unavailable; submission delayed. Provider={Provider}, Scope={Scope}, ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                        provider.Name,
                        rule.Scope,
                        mailToBeSent.ItemId,
                        mailToBeSent.ProjectKey,
                        mailToBeSent.TenantId,
                        mailToBeSent.OrganizationId);
                    return MailRateLimitResult.Rejected(
                        rule.Scope,
                        "ProviderRateLimiterUnavailable",
                        unavailableRetryAfterSeconds);
                }

                if (result.IsAllowed)
                {
                    continue;
                }

                var retryAfterSeconds = Math.Max(
                    GetRetryAfterSeconds(now, result.WindowEndUtc),
                    Math.Max(1, _configuration.GetValue<int?>("MailProviderRateLimiting:DefaultRetryAfterSeconds") ?? 60));

                _logger.LogWarning(
                    "Mail provider rate limited submission. Provider={Provider}, Scope={Scope}, LimiterKey={LimiterKey}, Used={Used}, Limit={Limit}, RetryAfterSeconds={RetryAfterSeconds}, ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}, SenderAddress={SenderAddress}",
                    provider.Name,
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

                return MailRateLimitResult.Rejected(rule.Scope, "ProviderRateLimitExceeded", retryAfterSeconds);
            }

            return MailRateLimitResult.Allowed();
        }

        private IEnumerable<RateLimitRule> BuildRules(
            MailToBeSent mailToBeSent,
            ProviderDescriptor provider,
            string providerConfigPath)
        {
            var tenantId = NormalizeScopeValue(mailToBeSent.TenantId);
            var senderAddress = NormalizeScopeValue(GetSenderAddress(mailToBeSent));
            var providerIdentity = GetProviderIdentity(mailToBeSent, provider);

            yield return CreateRule(
                "ProviderTenantMinute",
                $"mail-provider:{provider.Key}:tenant-minute:{tenantId}",
                $"{providerConfigPath}:PerTenantPerMinuteSubmissionLimit",
                600);

            yield return CreateRule(
                provider.IdentityScope,
                $"mail-provider:{provider.Key}:{provider.IdentityKey}-minute:{providerIdentity}",
                $"{providerConfigPath}:{provider.IdentityLimitKey}",
                provider.DefaultIdentityLimit);

            yield return CreateRule(
                "ProviderSenderMinute",
                $"mail-provider:{provider.Key}:sender-minute:{providerIdentity}:{senderAddress}",
                $"{providerConfigPath}:PerSenderPerMinuteSubmissionLimit",
                120);

            if (mailToBeSent.MailCategory == MailCategory.LargeAttachment)
            {
                yield return CreateRule(
                    "ProviderLargeAttachmentSenderMinute",
                    $"mail-provider:{provider.Key}:large-sender-minute:{providerIdentity}:{senderAddress}",
                    $"{providerConfigPath}:LargeAttachmentPerSenderPerMinuteSubmissionLimit",
                    30);
            }
        }

        private static ProviderDescriptor ResolveProvider(MailToBeSent mailToBeSent)
        {
            var configuration = mailToBeSent.MailServerConfiguration;
            if (configuration?.SmtpClient == SmtpClient.MsGraph)
            {
                return new("MicrosoftGraph", "graph", "client", "ProviderClientMinute",
                    "PerClientPerMinuteSubmissionLimit", 300);
            }

            if (IsAmazonSesHost(configuration?.Host))
            {
                return new("AmazonSes", "ses", "account", "ProviderAccountMinute",
                    "PerAccountPerMinuteSubmissionLimit", 600);
            }

            return new("Smtp", "smtp", "server", "ProviderServerMinute",
                "PerServerPerMinuteSubmissionLimit", 300);
        }

        private static bool IsAmazonSesHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            var normalizedHost = host.Trim().TrimEnd('.');
            return normalizedHost.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase) &&
                   normalizedHost.Contains("email-smtp.", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetProviderIdentity(MailToBeSent mailToBeSent, ProviderDescriptor provider)
        {
            var configuration = mailToBeSent.MailServerConfiguration;
            return provider.Key switch
            {
                "graph" => NormalizeScopeValue(configuration?.SenderUserName),
                "ses" => NormalizeScopeValue(configuration?.Host),
                _ => $"{NormalizeScopeValue(configuration?.Host)}:{configuration?.Port ?? 0}"
            };
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

        private sealed record ProviderDescriptor(
            string ConfigSection,
            string Key,
            string IdentityKey,
            string IdentityScope,
            string IdentityLimitKey,
            int DefaultIdentityLimit)
        {
            public string Name => ConfigSection;
        }
    }
}
