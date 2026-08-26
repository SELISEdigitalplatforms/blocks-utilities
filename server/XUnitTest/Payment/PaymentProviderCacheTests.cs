using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentProviderCacheTests
{
    [Fact]
    public async Task Caches_only_a_successfully_hydrated_provider()
    {
        var secrets =
            new Mock<IPaymentProviderSecretHydrator>();
        secrets.Setup(
                value => value.HydrateAsync(
                    It.IsAny<PaymentProvider>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var cache = CreateCache(secrets.Object);
        var loadCount = 0;

        Task<PaymentProvider?> Load()
        {
            loadCount++;

            return Task.FromResult<PaymentProvider?>(
                Provider());
        }

        var first = await cache.GetAsync(
            "tenant",
            null,
            "provider",
            Load);
        var second = await cache.GetAsync(
            "tenant",
            null,
            "provider",
            Load);

        first.Should().NotBeNull();
        second.Should().BeSameAs(first);
        loadCount.Should().Be(1);
        secrets.Verify(
            value => value.HydrateAsync(
                It.IsAny<PaymentProvider>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Does_not_cache_provider_when_hydration_fails()
    {
        var secrets =
            new Mock<IPaymentProviderSecretHydrator>();
        secrets.Setup(
                value => value.HydrateAsync(
                    It.IsAny<PaymentProvider>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var cache = CreateCache(secrets.Object);
        var loadCount = 0;

        Task<PaymentProvider?> Load()
        {
            loadCount++;

            return Task.FromResult<PaymentProvider?>(
                Provider());
        }

        var first = await cache.GetAsync(
            "tenant",
            null,
            "provider",
            Load);
        var second = await cache.GetAsync(
            "tenant",
            null,
            "provider",
            Load);

        first.Should().BeNull();
        second.Should().BeNull();
        loadCount.Should().Be(2);
    }

    [Fact]
    public async Task Throttles_repeated_forced_secret_refreshes()
    {
        var secrets =
            new Mock<IPaymentProviderSecretHydrator>();
        secrets.Setup(
                value => value.HydrateAsync(
                    It.IsAny<PaymentProvider>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var cache = CreateCache(secrets.Object);
        var loadCount = 0;

        Task<PaymentProvider?> Load()
        {
            loadCount++;

            return Task.FromResult<PaymentProvider?>(
                Provider());
        }

        await cache.GetAsync(
            "tenant",
            null,
            "provider",
            Load);
        await cache.RefreshAsync(
            "tenant",
            null,
            "provider",
            Load);
        await cache.RefreshAsync(
            "tenant",
            null,
            "provider",
            Load);

        loadCount.Should().Be(2);
    }

    [Fact]
    public async Task Keeps_last_valid_provider_when_refresh_fails()
    {
        var secrets =
            new Mock<IPaymentProviderSecretHydrator>();
        secrets.SetupSequence(
                value => value.HydrateAsync(
                    It.IsAny<PaymentProvider>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        var cache = CreateCache(secrets.Object);
        var firstProvider = Provider();

        var cached = await cache.GetAsync(
            "tenant",
            null,
            "provider",
            () => Task.FromResult<PaymentProvider?>(
                firstProvider));
        var refreshed = await cache.RefreshAsync(
            "tenant",
            null,
            "provider",
            () => Task.FromResult<PaymentProvider?>(
                Provider()));

        refreshed.Should().BeSameAs(cached);
    }

    /// <summary>
    /// Two organizations under one tenant may pay through different merchant accounts. A cache
    /// ignoring the organization would hand the first organization's configuration — and its
    /// credentials — to the second.
    /// </summary>
    [Fact]
    public async Task Each_organization_is_cached_separately()
    {
        var secrets = new Mock<IPaymentProviderSecretHydrator>();
        secrets.Setup(value => value.HydrateAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var cache = CreateCache(secrets.Object);

        var first = await cache.GetAsync(
            "tenant",
            "organization-1",
            "provider",
            () => Task.FromResult<PaymentProvider?>(
                new PaymentProvider { MerchantId = "merchant-1" }));
        var second = await cache.GetAsync(
            "tenant",
            "organization-2",
            "provider",
            () => Task.FromResult<PaymentProvider?>(
                new PaymentProvider { MerchantId = "merchant-2" }));

        first!.MerchantId.Should().Be("merchant-1");
        second!.MerchantId.Should().Be("merchant-2");
    }

    /// <summary>
    /// The tenant-level configuration an organization may fall back to is a separate entry
    /// again, so caching one never satisfies a lookup for the other.
    /// </summary>
    [Fact]
    public async Task An_organizations_entry_is_separate_from_the_tenants_own()
    {
        var secrets = new Mock<IPaymentProviderSecretHydrator>();
        secrets.Setup(value => value.HydrateAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var cache = CreateCache(secrets.Object);

        await cache.GetAsync(
            "tenant",
            null,
            "provider",
            () => Task.FromResult<PaymentProvider?>(
                new PaymentProvider { MerchantId = "tenant-merchant" }));

        var loaded = 0;
        var scoped = await cache.GetAsync(
            "tenant",
            "organization-1",
            "provider",
            () =>
            {
                loaded++;

                return Task.FromResult<PaymentProvider?>(
                    new PaymentProvider { MerchantId = "organization-merchant" });
            });

        loaded.Should().Be(1);
        scoped!.MerchantId.Should().Be("organization-merchant");
    }

    [Fact]
    public async Task Removing_a_provider_drops_every_organizations_entry()
    {
        var secrets = new Mock<IPaymentProviderSecretHydrator>();
        secrets.Setup(value => value.HydrateAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cache = CreateCache(secrets.Object);
        var loadCount = 0;

        Task<PaymentProvider?> Load()
        {
            loadCount++;

            return Task.FromResult<PaymentProvider?>(Provider());
        }

        // One tenant-level configuration, cached under three different askers.
        await cache.GetAsync("tenant", "org-a", "provider", Load);
        await cache.GetAsync("tenant", "org-b", "provider", Load);
        await cache.GetAsync("tenant", null, "provider", Load);
        loadCount.Should().Be(3);

        cache.RemoveAll("tenant", "provider");

        await cache.GetAsync("tenant", "org-a", "provider", Load);
        await cache.GetAsync("tenant", "org-b", "provider", Load);
        await cache.GetAsync("tenant", null, "provider", Load);

        // All three reloaded: rotated credentials must not survive anywhere, already decrypted.
        loadCount.Should().Be(6);
    }

    [Fact]
    public async Task Removing_a_provider_leaves_other_tenants_and_providers_alone()
    {
        var secrets = new Mock<IPaymentProviderSecretHydrator>();
        secrets.Setup(value => value.HydrateAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cache = CreateCache(secrets.Object);
        var loadCount = 0;

        Task<PaymentProvider?> Load()
        {
            loadCount++;

            return Task.FromResult<PaymentProvider?>(Provider());
        }

        await cache.GetAsync("tenant", "org-a", "other-provider", Load);
        await cache.GetAsync("other-tenant", "org-a", "provider", Load);
        loadCount.Should().Be(2);

        cache.RemoveAll("tenant", "provider");

        await cache.GetAsync("tenant", "org-a", "other-provider", Load);
        await cache.GetAsync("other-tenant", "org-a", "provider", Load);
        loadCount.Should().Be(2);
    }

    private static PaymentProviderCache CreateCache(
        IPaymentProviderSecretHydrator secrets)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(value => value.CurrentValue)
            .Returns(
                new PaymentOptions
                {
                    ProviderCacheSeconds = 120,
                    ProviderSecretRefreshThrottleSeconds = 30
                });

        return new PaymentProviderCache(
            options.Object,
            secrets);
    }

    private static PaymentProvider Provider() =>
        new()
        {
            TenantId = "tenant",
            ProviderName = "provider"
        };
}
