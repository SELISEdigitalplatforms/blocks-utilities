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
            "provider",
            Load);
        var second = await cache.GetAsync(
            "tenant",
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
            "provider",
            Load);
        var second = await cache.GetAsync(
            "tenant",
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
            "provider",
            Load);
        await cache.RefreshAsync(
            "tenant",
            "provider",
            Load);
        await cache.RefreshAsync(
            "tenant",
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
            "provider",
            () => Task.FromResult<PaymentProvider?>(
                firstProvider));
        var refreshed = await cache.RefreshAsync(
            "tenant",
            "provider",
            () => Task.FromResult<PaymentProvider?>(
                Provider()));

        refreshed.Should().BeSameAs(cached);
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
