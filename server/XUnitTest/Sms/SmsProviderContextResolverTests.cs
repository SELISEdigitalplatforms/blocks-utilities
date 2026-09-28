using Blocks.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Sms.DomainService.Entities;
using Sms.DomainService.Services;

namespace XUnitTest.Sms;

public class SmsProviderContextResolverTests
{
    private readonly Mock<ISecretService> _secrets = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public SmsProviderContextResolverTests()
    {
        _secrets.Setup(s => s.GetValueAsync("secret-1", It.IsAny<CancellationToken>())).ReturnsAsync("key");
    }

    [Fact]
    public async Task RepeatedSendsReadTheSecretOnce()
    {
        var configuration = Configuration(DateTime.UtcNow);
        var resolver = new SmsProviderContextResolver(_secrets.Object, _cache);

        (await resolver.ResolveAsync("tenant-a", configuration)).ApiKey.Should().Be("key");
        (await resolver.ResolveAsync("tenant-a", configuration)).ApiKey.Should().Be("key");

        _secrets.Verify(s => s.GetValueAsync("secret-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ARotationThroughTheSaveEndpointMissesTheCache()
    {
        var resolver = new SmsProviderContextResolver(_secrets.Object, _cache);
        var saved = DateTime.UtcNow;
        await resolver.ResolveAsync("tenant-a", Configuration(saved));

        _secrets.Setup(s => s.GetValueAsync("secret-1", It.IsAny<CancellationToken>())).ReturnsAsync("rotated");

        (await resolver.ResolveAsync("tenant-a", Configuration(saved.AddSeconds(1)))).ApiKey.Should().Be("rotated");
    }

    [Fact]
    public async Task AnotherTenantNeverGetsACachedKey()
    {
        var configuration = Configuration(DateTime.UtcNow);
        var resolver = new SmsProviderContextResolver(_secrets.Object, _cache);

        await resolver.ResolveAsync("tenant-a", configuration);
        await resolver.ResolveAsync("tenant-b", configuration);

        _secrets.Verify(s => s.GetValueAsync("secret-1", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AFailedReadIsNotCached()
    {
        var configuration = Configuration(DateTime.UtcNow);
        var resolver = new SmsProviderContextResolver(_secrets.Object, _cache);
        _secrets.SetupSequence(s => s.GetValueAsync("secret-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty)
            .ReturnsAsync("key");

        await resolver.Invoking(r => r.ResolveAsync("tenant-a", configuration)).Should().ThrowAsync<InvalidOperationException>();
        (await resolver.ResolveAsync("tenant-a", configuration)).ApiKey.Should().Be("key");
    }

    private static SmsProviderConfiguration Configuration(DateTime lastUpdated) =>
        new() { ApiKeySecretId = "secret-1", LastUpdatedDate = lastUpdated };
}
