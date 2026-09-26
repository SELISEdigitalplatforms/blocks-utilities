using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Services;
using StackExchange.Redis;

namespace XUnitTest.Sms;

public class SmsSafetyTests
{
    private static readonly string[] OneRecipient = ["+41790000000"];

    [Fact]
    public void SpamFilter_BlocksBlockedTermWithUrl()
    {
        var result = new SuspiciousMessageService().Analyze("reset your password at https://example.com", OneRecipient, new SmsSpamFilterSettings());

        result.ShouldBlock.Should().BeTrue();
    }

    [Theory]
    [InlineData(SmsUrlPolicy.Allow, SmsRiskLevel.Low)]
    [InlineData(SmsUrlPolicy.Flag, SmsRiskLevel.High)]
    [InlineData(SmsUrlPolicy.Block, SmsRiskLevel.Blocked)]
    public void SpamFilter_UrlPolicyComesFromConfiguration(SmsUrlPolicy policy, SmsRiskLevel expected)
    {
        var settings = new SmsSpamFilterSettings { UrlPolicy = policy, BlockedTerms = [] };

        var result = new SuspiciousMessageService().Analyze("see https://example.com", OneRecipient, settings);

        result.RiskLevel.Should().Be(expected);
    }

    [Fact]
    public void SpamFilter_UsesConfiguredRecipientCeiling_AndCanBeDisabled()
    {
        var numbers = new[] { "+41790000001", "+41790000002", "+41790000003" };
        var service = new SuspiciousMessageService();

        service.Analyze("hi", numbers, new SmsSpamFilterSettings { MaxRecipients = 2 }).ShouldBlock.Should().BeTrue();
        service.Analyze("hi", numbers, new SmsSpamFilterSettings { MaxRecipients = 2, Enabled = false }).ShouldBlock.Should().BeFalse();
    }

    [Fact]
    public void RetryPolicy_ReturnsFutureRetry()
    {
        var now = DateTime.UtcNow;

        new SmsRetryPolicy().GetNextRetryAt(2, now).Should().BeAfter(now);
    }

    [Fact]
    public async Task RateLimiter_CountsOneSmsPerRecipientAgainstTheTenant()
    {
        var (limiter, database) = CreateLimiter(_ => 1);
        var settings = new SmsRateLimitSettings { TenantMaxPerWindow = 10, RecipientMaxPerWindow = 5 };

        var result = await limiter.CheckAsync("tenant-a", ["+41790000001", "+41790000002"], settings);

        result.IsAllowed.Should().BeTrue();
        database.Verify(db => db.StringIncrementAsync(It.Is<RedisKey>(k => k.ToString().Contains(":tenant:")), 2, It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RateLimiter_RecipientLimitRefusesWithoutSpendingTenantAllowance()
    {
        var (limiter, database) = CreateLimiter(key => key.Contains(":recipient:") ? 6 : 1);
        var settings = new SmsRateLimitSettings { TenantMaxPerWindow = 100, RecipientMaxPerWindow = 5 };

        var result = await limiter.CheckAsync("tenant-a", OneRecipient, settings);

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Recipient");
        database.Verify(db => db.StringIncrementAsync(It.Is<RedisKey>(k => k.ToString().Contains(":tenant:")), It.IsAny<long>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task RateLimiter_TenantLimitIsIndependentOfRecipientLimit()
    {
        var (limiter, _) = CreateLimiter(key => key.Contains(":tenant:") ? 11 : 1);
        var settings = new SmsRateLimitSettings { TenantMaxPerWindow = 10, RecipientMaxPerWindow = 50 };

        var result = await limiter.CheckAsync("tenant-a", OneRecipient, settings);

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Tenant");
    }

    [Fact]
    public async Task RateLimiter_FailsClosedWhenRedisIsDown()
    {
        var cache = new Mock<ICacheClient>();
        cache.Setup(c => c.CacheDatabase()).Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var result = await new SmsRateLimiter(cache.Object, NullLogger<SmsRateLimiter>.Instance)
            .CheckAsync("tenant-a", OneRecipient, new SmsRateLimitSettings());

        result.IsAllowed.Should().BeFalse();
    }

    private static (SmsRateLimiter Limiter, Mock<IDatabase> Database) CreateLimiter(Func<string, long> countFor)
    {
        var cache = new Mock<ICacheClient>();
        var database = new Mock<IDatabase>();
        cache.Setup(c => c.CacheDatabase()).Returns(database.Object);
        database
            .Setup(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, long _, CommandFlags _) => countFor(key.ToString()));
        return (new SmsRateLimiter(cache.Object, NullLogger<SmsRateLimiter>.Instance), database);
    }
}
