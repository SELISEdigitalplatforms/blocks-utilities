using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class ProviderTokenEncryptionKeyRingProviderTests
{
    private static readonly PaymentEncryptionScope First =
        new("tenant", "organization-1");

    private static readonly PaymentEncryptionScope Second =
        new("tenant", "organization-2");

    /// <summary>
    /// The point of the whole change: two organizations under one tenant must reach different
    /// key material, so a compromise of one cannot open the other's cards.
    /// </summary>
    [Fact]
    public async Task Each_organization_resolves_its_own_key_ring()
    {
        var vault = Vault(
            (PaymentKeyRingSecretName.Create(First), Serialized("key-a", 1)),
            (PaymentKeyRingSecretName.Create(Second), Serialized("key-b", 2)));
        using var provider = Provider(vault, out _);

        var first = await provider.GetAsync(First);
        var second = await provider.GetAsync(Second);

        first.ActiveKeyId.Should().Be("key-a");
        second.ActiveKeyId.Should().Be("key-b");
    }

    /// <summary>
    /// One organization's missing ring must not take the service down — the old behaviour, where
    /// a single ring failed the whole host, is exactly what per-organization rings replace.
    /// </summary>
    [Fact]
    public async Task A_missing_ring_fails_only_its_own_organization()
    {
        var vault = Vault(
            (PaymentKeyRingSecretName.Create(First), Serialized("key-a", 1)));
        using var provider = Provider(vault, out _, fallBackToShared: false);

        var healthy = await provider.GetAsync(First);
        var broken = await provider.GetAsync(Second);

        healthy.ActiveKeyId.Should().Be("key-a");
        broken.ActiveKeyId.Should().BeEmpty();
        broken.TryGetKey("key-a", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_ring_is_read_once_and_then_served_from_cache()
    {
        var vault = Vault(
            (PaymentKeyRingSecretName.Create(First), Serialized("key-a", 1)));
        using var provider = Provider(vault, out _);

        await provider.GetAsync(First);
        await provider.GetAsync(First);

        vault.Verify(
            value => value.ProcessSecretsAsync(It.IsAny<List<string>>()),
            Times.Once);
    }

    /// <summary>
    /// Without expiry a rotated ring is never picked up by a running process, so rotation looks
    /// like it worked and silently did not.
    /// </summary>
    [Fact]
    public async Task An_expired_entry_is_re_read_so_rotation_is_picked_up()
    {
        var secretName = PaymentKeyRingSecretName.Create(First);
        var vault = new Mock<IVault>();
        vault.SetupSequence(
                value => value.ProcessSecretsAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [secretName] = Serialized("key-a", 1)
            })
            .ReturnsAsync(new Dictionary<string, string>
            {
                [secretName] = Serialized("key-b", 2)
            });

        using var provider = Provider(vault, out var time);

        (await provider.GetAsync(First)).ActiveKeyId.Should().Be("key-a");

        time.Advance(TimeSpan.FromSeconds(301));

        (await provider.GetAsync(First)).ActiveKeyId.Should().Be("key-b");
    }

    /// <summary>
    /// An evicted ring is disposed — that is what zeroes its key bytes — but only after a grace
    /// period, because a caller that fetched it a moment ago may still be encrypting with it.
    /// </summary>
    [Fact]
    public async Task An_evicted_ring_survives_the_grace_period_before_disposal()
    {
        var secretName = PaymentKeyRingSecretName.Create(First);
        var vault = new Mock<IVault>();
        vault.SetupSequence(
                value => value.ProcessSecretsAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [secretName] = Serialized("key-a", 1)
            })
            .ReturnsAsync(new Dictionary<string, string>
            {
                [secretName] = Serialized("key-b", 2)
            });

        using var provider = Provider(vault, out var time);

        var evicted = await provider.GetAsync(First);

        time.Advance(TimeSpan.FromSeconds(301));
        await provider.GetAsync(First);

        // Still usable: the previous holder has not been cut off mid-operation.
        evicted.TryGetKey("key-a", out var key).Should().BeTrue();
        key.Should().NotBeEmpty();
    }

    /// <summary>
    /// During migration a scope with no ring of its own keeps working on the pre-migration
    /// shared ring, so deploying scoped rings does not break tenants not yet provisioned.
    /// </summary>
    [Fact]
    public async Task An_unprovisioned_scope_falls_back_to_the_shared_ring()
    {
        var vault = Vault(
            (PaymentKeyRingSecretName.SharedSecretName, Serialized("shared", 3)));
        using var provider = Provider(vault, out _);

        var keyRing = await provider.GetAsync(First);

        keyRing.ActiveKeyId.Should().Be("shared");
    }

    /// <summary>
    /// The diagnostic reports the fallback rather than a clean bill of health: the scope works,
    /// but is not yet isolated, and that difference is the whole migration.
    /// </summary>
    [Fact]
    public async Task The_health_check_reports_a_scope_still_on_the_shared_ring()
    {
        var vault = Vault(
            (PaymentKeyRingSecretName.SharedSecretName, Serialized("shared", 3)));
        using var provider = Provider(vault, out _);

        var health = await provider.CheckAsync(First);

        health.IsReadable.Should().BeTrue();
        health.UsedSharedKeyRing.Should().BeTrue();
        health.SecretName.Should().Be(PaymentKeyRingSecretName.Create(First));
    }

    [Fact]
    public async Task The_health_check_reports_an_unreadable_ring()
    {
        var vault = Vault();
        using var provider = Provider(vault, out _, fallBackToShared: false);

        var health = await provider.CheckAsync(Second);

        health.IsReadable.Should().BeFalse();
        health.FailureReason.Should().Be("key_ring_unavailable");
    }

    private static ProviderTokenEncryptionKeyRingProvider Provider(
        Mock<IVault> vault,
        out ControlledTimeProvider time,
        bool fallBackToShared = true)
    {
        time = new ControlledTimeProvider(DateTimeOffset.UnixEpoch);

        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(value => value.CurrentValue)
            .Returns(new PaymentOptions
            {
                FallBackToSharedEncryptionKeyRing = fallBackToShared
            });

        return new ProviderTokenEncryptionKeyRingProvider(
            vault.Object,
            options.Object,
            NullLogger<ProviderTokenEncryptionKeyRingProvider>.Instance,
            time);
    }

    private static Mock<IVault> Vault(
        params (string SecretName, string Serialized)[] secrets)
    {
        var known = secrets.ToDictionary(
            secret => secret.SecretName,
            secret => secret.Serialized,
            StringComparer.Ordinal);
        var vault = new Mock<IVault>();

        vault.Setup(
                value => value.ProcessSecretsAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(
                (List<string> names) => names
                    .Where(known.ContainsKey)
                    .ToDictionary(name => name, name => known[name]));

        return vault;
    }

    private static string Serialized(string keyId, byte fill) =>
        JsonSerializer.Serialize(
            new ProviderTokenEncryptionKeyRingSecret
            {
                ActiveKeyId = keyId,
                Keys = new Dictionary<string, string>
                {
                    [keyId] = Convert.ToBase64String(
                        Enumerable.Repeat(fill, 32).ToArray())
                }
            });
}
