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
    [Fact]
    public void SuspiciousMessageService_ShouldBlockSensitiveUrlMessages()
    {
        var service = new SuspiciousMessageService();

        var result = service.Analyze("reset your password at https://example.com", ["+41790000000"]);

        result.RiskLevel.Should().Be(SmsRiskLevel.Blocked);
        result.ShouldBlock.Should().BeTrue();
    }

    [Fact]
    public void SmsRetryPolicy_ShouldReturnFutureRetry()
    {
        var policy = new SmsRetryPolicy();
        var now = DateTime.UtcNow;

        var retryAt = policy.GetNextRetryAt(2, now);

        retryAt.Should().BeAfter(now);
    }

    [Fact]
    public async Task SmsRateLimiter_ShouldAllow_WhenRedisCountersAreWithinLimit()
    {
        var cache = new Mock<ICacheClient>();
        var database = new Mock<IDatabase>();
        cache.Setup(client => client.CacheDatabase()).Returns(database.Object);
        database.Setup(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), 1, It.IsAny<CommandFlags>())).ReturnsAsync(1);
        database.Setup(db => db.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);

        var limiter = new SmsRateLimiter(cache.Object, NullLogger<SmsRateLimiter>.Instance);

        var result = await limiter.CheckAsync(CreateMessage(), CreateConfiguration(maxPerWindow: 2));

        result.IsAllowed.Should().BeTrue();
        database.Verify(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), 1, It.IsAny<CommandFlags>()), Times.Exactly(2));
        database.Verify(db => db.KeyExpireAsync(It.IsAny<RedisKey>(), TimeSpan.FromSeconds(60), ExpireWhen.Always, It.IsAny<CommandFlags>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SmsRateLimiter_ShouldBlock_WhenTenantCounterExceedsLimit()
    {
        var cache = new Mock<ICacheClient>();
        var database = new Mock<IDatabase>();
        cache.Setup(client => client.CacheDatabase()).Returns(database.Object);
        database.Setup(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), 1, It.IsAny<CommandFlags>())).ReturnsAsync(3);

        var limiter = new SmsRateLimiter(cache.Object, NullLogger<SmsRateLimiter>.Instance);

        var result = await limiter.CheckAsync(CreateMessage(), CreateConfiguration(maxPerWindow: 2));

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Be("Tenant SMS rate limit exceeded.");
        database.Verify(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), 1, It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SmsRateLimiter_ShouldFailClosed_WhenRedisIsUnavailable()
    {
        var cache = new Mock<ICacheClient>();
        cache.Setup(client => client.CacheDatabase()).Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis unavailable"));

        var limiter = new SmsRateLimiter(cache.Object, NullLogger<SmsRateLimiter>.Instance);

        var result = await limiter.CheckAsync(CreateMessage(), CreateConfiguration(maxPerWindow: 2));

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Be("SMS rate limiter is unavailable.");
    }

    private static SmsMessage CreateMessage()
    {
        return new SmsMessage
        {
            ProjectKey = "project-a",
            TenantId = "tenant-a",
            DestinationNumbers = ["+41790000000"]
        };
    }

    private static SmsProviderConfiguration CreateConfiguration(int maxPerWindow)
    {
        return new SmsProviderConfiguration
        {
            RateLimitMaxPerWindow = maxPerWindow,
            RateLimitWindowSeconds = 60
        };
    }
}

