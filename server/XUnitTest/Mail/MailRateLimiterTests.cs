using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Mail
{
    public class MailRateLimiterTests
    {
        [Fact]
        public async Task CheckAsync_UsesDistinctRecipientCountAsCost()
        {
            var claims = new List<MailRateLimitCounterClaim>();
            var repository = new Mock<IMailRepository>();
            repository
                .Setup(x => x.TryIncrementRateLimitCounterAsync(It.IsAny<MailRateLimitCounterClaim>()))
                .Callback<MailRateLimitCounterClaim>(claim => claims.Add(claim))
                .ReturnsAsync((MailRateLimitCounterClaim claim) => new MailRateLimitCounterClaimResult
                {
                    IsAllowed = true,
                    Used = claim.Cost,
                    Limit = claim.Limit,
                    WindowEndUtc = claim.WindowEndUtc
                });

            var limiter = new MailRateLimiter(repository.Object, CreateConfiguration(), NullLogger<MailRateLimiter>.Instance);

            var result = await limiter.CheckAsync(new MailToBeSent
            {
                TenantId = "tenant-a",
                ProjectKey = "project-a",
                OrganizationId = "org-a",
                SenderAddress = "sender@example.com",
                To = ["one@example.com", "one@example.com"],
                Cc = ["two@example.com"],
                Bcc = []
            });

            Assert.True(result.IsAllowed);
            Assert.NotEmpty(claims);
            Assert.All(claims, claim => Assert.Equal(2, claim.Cost));
        }

        [Fact]
        public async Task CheckAsync_ReturnsRejected_WhenProjectLimitIsExceeded()
        {
            var repository = new Mock<IMailRepository>();
            repository
                .Setup(x => x.TryIncrementRateLimitCounterAsync(It.IsAny<MailRateLimitCounterClaim>()))
                .ReturnsAsync((MailRateLimitCounterClaim claim) => new MailRateLimitCounterClaimResult
                {
                    IsAllowed = !claim.LimiterKey.Contains("project-minute"),
                    Used = claim.Limit,
                    Limit = claim.Limit,
                    WindowEndUtc = claim.WindowEndUtc
                });

            var limiter = new MailRateLimiter(repository.Object, CreateConfiguration(), NullLogger<MailRateLimiter>.Instance);

            var result = await limiter.CheckAsync(new MailToBeSent
            {
                TenantId = "tenant-a",
                ProjectKey = "project-a",
                OrganizationId = "org-a",
                SenderAddress = "sender@example.com",
                To = ["one@example.com"],
                Cc = [],
                Bcc = []
            });

            Assert.False(result.IsAllowed);
            Assert.Equal("ProjectMinute", result.Scope);
            Assert.Equal("MailRateLimitExceeded", result.Reason);
            Assert.True(result.RetryAfterSeconds > 0);
        }

        [Fact]
        public async Task ProviderCheckAsync_ReturnsRejected_WhenSenderProviderLimitIsExceeded()
        {
            var repository = new Mock<IMailRepository>();
            repository
                .Setup(x => x.TryIncrementRateLimitCounterAsync(It.IsAny<MailRateLimitCounterClaim>()))
                .ReturnsAsync((MailRateLimitCounterClaim claim) => new MailRateLimitCounterClaimResult
                {
                    IsAllowed = !claim.LimiterKey.Contains("sender-minute"),
                    Used = claim.Limit,
                    Limit = claim.Limit,
                    WindowEndUtc = claim.WindowEndUtc
                });

            var limiter = new MailProviderRateLimiter(repository.Object, CreateConfiguration(), NullLogger<MailProviderRateLimiter>.Instance);

            var result = await limiter.CheckAsync(new MailToBeSent
            {
                TenantId = "tenant-a",
                SenderAddress = "sender@example.com",
                MailCategory = MailCategory.NoAttachment,
                MailServerConfiguration = new MailServerConfiguration
                {
                    SenderUserName = "client-a",
                    SenderAddress = "sender@example.com"
                }
            });

            Assert.False(result.IsAllowed);
            Assert.Equal("ProviderSenderMinute", result.Scope);
            Assert.Equal("MicrosoftGraphProviderRateLimitExceeded", result.Reason);
            Assert.True(result.RetryAfterSeconds >= 60);
        }

        private static IConfiguration CreateConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MailRateLimiting:Enabled"] = "true",
                    ["MicrosoftGraphProviderRateLimiting:Enabled"] = "true",
                    ["MicrosoftGraphProviderRateLimiting:DefaultRetryAfterSeconds"] = "60"
                })
                .Build();
        }
    }
}
