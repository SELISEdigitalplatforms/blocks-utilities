using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentProviderCacheBranchTests
{
    private static PaymentProviderCache Cache(IPaymentProviderSecretHydrator secrets)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(value => value.CurrentValue)
            .Returns(new PaymentOptions
            {
                ProviderCacheSeconds = 120,
                ProviderSecretRefreshThrottleSeconds = 30
            });
        return new PaymentProviderCache(options.Object, secrets);
    }

    private static IPaymentProviderSecretHydrator HydratingSecrets()
    {
        var secrets = new Mock<IPaymentProviderSecretHydrator>();
        secrets.Setup(s => s.HydrateAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return secrets.Object;
    }

    [Fact]
    public async Task Cached_provider_is_served_without_reinvoking_the_loader()
    {
        var cache = Cache(HydratingSecrets());
        var loads = 0;
        Task<PaymentProvider?> Loader()
        {
            loads++;
            return Task.FromResult<PaymentProvider?>(
                new PaymentProvider { TenantId = "tenant", ProviderName = "provider" });
        }

        var first = await cache.GetAsync("tenant", null, "provider", Loader);
        var second = await cache.GetAsync("tenant", null, "provider", Loader);

        first.Should().NotBeNull();
        second.Should().BeSameAs(first);
        loads.Should().Be(1);
    }

    [Fact]
    public async Task Remove_evicts_a_cached_provider()
    {
        var cache = Cache(HydratingSecrets());
        var loads = 0;
        Task<PaymentProvider?> Loader()
        {
            loads++;
            return Task.FromResult<PaymentProvider?>(
                new PaymentProvider { TenantId = "tenant", ProviderName = "provider" });
        }

        await cache.GetAsync("tenant", null, "provider", Loader);
        cache.Remove("tenant", null, "provider");
        await cache.GetAsync("tenant", null, "provider", Loader);

        loads.Should().Be(2);
    }

    [Fact]
    public async Task Capacity_pressure_triggers_eviction_sweep()
    {
        var cache = Cache(HydratingSecrets());

        for (var index = 0; index <= 1000; index++)
        {
            var key = index;
            await cache.GetAsync(
                "tenant",
                null,
                $"provider-{key}",
                () => Task.FromResult<PaymentProvider?>(
                    new PaymentProvider
                    {
                        TenantId = "tenant",
                        ProviderName = $"provider-{key}"
                    }));
        }

        // The 1001st insert forces RemoveExpired, which clears the non-expired
        // cache at capacity; the entry just written is still resolvable.
        var reloaded = await cache.GetAsync(
            "tenant",
            null,
            "provider-1000",
            () => Task.FromResult<PaymentProvider?>(
                new PaymentProvider
                {
                    TenantId = "tenant",
                    ProviderName = "provider-1000"
                }));

        reloaded.Should().NotBeNull();
    }

    [Fact]
    public async Task Provider_that_fails_hydration_is_not_cached()
    {
        var secrets = new Mock<IPaymentProviderSecretHydrator>();
        secrets.Setup(s => s.HydrateAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var cache = Cache(secrets.Object);

        var result = await cache.GetAsync(
            "tenant", null, "provider",
            () => Task.FromResult<PaymentProvider?>(
                new PaymentProvider { TenantId = "tenant", ProviderName = "provider" }));

        result.Should().BeNull();
    }
}
