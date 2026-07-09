using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
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
            var counterStore = new Mock<IMailRateLimitCounterStore>();
            counterStore
                .Setup(x => x.TryClaimAsync(It.IsAny<MailRateLimitCounterClaim>(), It.IsAny<CancellationToken>()))
                .Callback<MailRateLimitCounterClaim, CancellationToken>((claim, _) => claims.Add(claim))
                .ReturnsAsync((MailRateLimitCounterClaim claim, CancellationToken _) => new MailRateLimitCounterClaimResult
                {
                    IsAllowed = true,
                    Used = claim.Cost,
                    Limit = claim.Limit,
                    WindowEndUtc = claim.WindowEndUtc
                });

            var limiter = new MailRateLimiter(counterStore.Object, CreateConfiguration(), NullLogger<MailRateLimiter>.Instance);

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
            var counterStore = new Mock<IMailRateLimitCounterStore>();
            counterStore
                .Setup(x => x.TryClaimAsync(It.IsAny<MailRateLimitCounterClaim>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((MailRateLimitCounterClaim claim, CancellationToken _) => new MailRateLimitCounterClaimResult
                {
                    IsAllowed = !claim.LimiterKey.Contains("project-minute"),
                    Used = claim.Limit,
                    Limit = claim.Limit,
                    WindowEndUtc = claim.WindowEndUtc
                });

            var limiter = new MailRateLimiter(counterStore.Object, CreateConfiguration(), NullLogger<MailRateLimiter>.Instance);

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
            var counterStore = new Mock<IMailRateLimitCounterStore>();
            counterStore
                .Setup(x => x.TryClaimAsync(It.IsAny<MailRateLimitCounterClaim>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((MailRateLimitCounterClaim claim, CancellationToken _) => new MailRateLimitCounterClaimResult
                {
                    IsAllowed = !claim.LimiterKey.Contains("sender-minute"),
                    Used = claim.Limit,
                    Limit = claim.Limit,
                    WindowEndUtc = claim.WindowEndUtc
                });

            var limiter = new MailProviderRateLimiter(counterStore.Object, CreateConfiguration(), NullLogger<MailProviderRateLimiter>.Instance);

            var result = await limiter.CheckAsync(new MailToBeSent
            {
                TenantId = "tenant-a",
                SenderAddress = "sender@example.com",
                MailCategory = MailCategory.NoAttachment,
                MailServerConfiguration = new MailServerConfiguration
                {
                    SmtpClient = SmtpClient.MsGraph,
                    SenderUserName = "client-a",
                    SenderAddress = "sender@example.com"
                }
            });

            Assert.False(result.IsAllowed);
            Assert.Equal("ProviderSenderMinute", result.Scope);
            Assert.Equal("ProviderRateLimitExceeded", result.Reason);
            Assert.True(result.RetryAfterSeconds >= 60);
        }

        [Theory]
        [InlineData(SmtpClient.MsGraph, "graph", "ProviderClientMinute", "")]
        [InlineData(SmtpClient.MsMailKit, "ses", "ProviderAccountMinute", "email-smtp.eu-west-1.amazonaws.com")]
        [InlineData(SmtpClient.MsMailKit, "smtp", "ProviderServerMinute", "smtp.example.com")]
        public async Task ProviderCheckAsync_UsesProviderSpecificRules(
            SmtpClient smtpClient,
            string expectedProviderKey,
            string expectedIdentityScope,
            string host)
        {
            var claims = new List<MailRateLimitCounterClaim>();
            var counterStore = new Mock<IMailRateLimitCounterStore>();
            counterStore
                .Setup(x => x.TryClaimAsync(It.IsAny<MailRateLimitCounterClaim>(), It.IsAny<CancellationToken>()))
                .Callback<MailRateLimitCounterClaim, CancellationToken>((claim, _) => claims.Add(claim))
                .ReturnsAsync((MailRateLimitCounterClaim claim, CancellationToken _) => new MailRateLimitCounterClaimResult
                {
                    IsAllowed = true,
                    Used = claim.Cost,
                    Limit = claim.Limit,
                    WindowEndUtc = claim.WindowEndUtc
                });
            var limiter = new MailProviderRateLimiter(
                counterStore.Object,
                CreateConfiguration(),
                NullLogger<MailProviderRateLimiter>.Instance);

            var result = await limiter.CheckAsync(new MailToBeSent
            {
                TenantId = "tenant-a",
                SenderAddress = "sender@example.com",
                MailServerConfiguration = new MailServerConfiguration
                {
                    SmtpClient = smtpClient,
                    Host = host,
                    Port = 587,
                    SenderUserName = "credential-a"
                }
            });

            Assert.True(result.IsAllowed);
            Assert.Contains(claims, claim => claim.LimiterKey.Contains($"mail-provider:{expectedProviderKey}:"));
            Assert.Contains(claims, claim => claim.LimiterKey.Contains(
                expectedIdentityScope switch
                {
                    "ProviderClientMinute" => ":client-minute:",
                    "ProviderAccountMinute" => ":account-minute:",
                    _ => ":server-minute:"
                }));
        }

        [Fact]
        public async Task CheckAsync_WhenRedisUnavailable_FailsClosed()
        {
            var counterStore = new Mock<IMailRateLimitCounterStore>();
            counterStore
                .Setup(x => x.TryClaimAsync(It.IsAny<MailRateLimitCounterClaim>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis unavailable"));
            var limiter = new MailRateLimiter(
                counterStore.Object,
                CreateConfiguration(),
                NullLogger<MailRateLimiter>.Instance);

            var result = await limiter.CheckAsync(new MailToBeSent
            {
                TenantId = "tenant-a",
                ProjectKey = "project-a",
                To = ["one@example.com"],
                Cc = [],
                Bcc = []
            });

            Assert.False(result.IsAllowed);
            Assert.Equal("MailRateLimiterUnavailable", result.Reason);
        }

        [Fact]
        public async Task ProviderCheckAsync_WhenRedisUnavailable_DelaysSubmission()
        {
            var counterStore = new Mock<IMailRateLimitCounterStore>();
            counterStore
                .Setup(x => x.TryClaimAsync(It.IsAny<MailRateLimitCounterClaim>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis unavailable"));
            var limiter = new MailProviderRateLimiter(
                counterStore.Object,
                CreateConfiguration(),
                NullLogger<MailProviderRateLimiter>.Instance);

            var result = await limiter.CheckAsync(new MailToBeSent
            {
                TenantId = "tenant-a",
                SenderAddress = "sender@example.com",
                MailServerConfiguration = new MailServerConfiguration
                {
                    SenderUserName = "client-a"
                }
            });

            Assert.False(result.IsAllowed);
            Assert.Equal("ProviderRateLimiterUnavailable", result.Reason);
        }

        [Fact]
        public async Task GenesisCacheStore_WhenWithinLimit_PersistsCounterWithExpiry()
        {
            var cacheClient = new Mock<ICacheClient>();
            cacheClient
                .Setup(x => x.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync("2");
            cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), "5", It.IsAny<long>()))
                .ReturnsAsync(true);
            var store = new GenesisCacheMailRateLimitCounterStore(cacheClient.Object);
            var now = DateTime.UtcNow;

            var result = await store.TryClaimAsync(new MailRateLimitCounterClaim
            {
                LimiterKey = "mail-domain:tenant-minute:tenant-a",
                WindowStartUtc = now.AddSeconds(-1),
                WindowEndUtc = now.AddSeconds(30),
                Cost = 3,
                Limit = 10
            });

            Assert.True(result.IsAllowed);
            Assert.Equal(5, result.Used);
            cacheClient.Verify(x => x.AddStringValueAsync(
                It.Is<string>(key => key.StartsWith("blocks:mail-domain:tenant-minute:tenant-a:")),
                "5",
                It.Is<long>(ttl => ttl > 0 && ttl <= 30)), Times.Once);
        }

        [Fact]
        public async Task GenesisCacheStore_WhenLimitExceeded_DoesNotOverwriteCounter()
        {
            var cacheClient = new Mock<ICacheClient>();
            cacheClient
                .Setup(x => x.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync("9");
            var store = new GenesisCacheMailRateLimitCounterStore(cacheClient.Object);
            var now = DateTime.UtcNow;

            var result = await store.TryClaimAsync(new MailRateLimitCounterClaim
            {
                LimiterKey = "mail-domain:tenant-minute:tenant-a",
                WindowStartUtc = now,
                WindowEndUtc = now.AddMinutes(1),
                Cost = 2,
                Limit = 10
            });

            Assert.False(result.IsAllowed);
            Assert.Equal(11, result.Used);
            cacheClient.Verify(x => x.AddStringValueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>()), Times.Never);
        }

        private static IConfiguration CreateConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MailRateLimiting:Enabled"] = "true",
                    ["MailProviderRateLimiting:Enabled"] = "true",
                    ["MailProviderRateLimiting:DefaultRetryAfterSeconds"] = "60",
                    ["MailProviderRateLimiting:MicrosoftGraph:Enabled"] = "true",
                    ["MailProviderRateLimiting:AmazonSes:Enabled"] = "true",
                    ["MailProviderRateLimiting:Smtp:Enabled"] = "true"
                })
                .Build();
        }
    }
}
