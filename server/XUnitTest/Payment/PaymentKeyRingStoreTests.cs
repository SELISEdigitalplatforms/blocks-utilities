using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentKeyRingStoreTests
{
    private const string TenantId = "tenant-1";
    private static readonly PaymentEncryptionScope Scope =
        new(TenantId, "organization-2");

    private static readonly string SharedKey =
        Convert.ToBase64String(Enumerable.Repeat((byte)3, 32).ToArray());

    private readonly Mock<IKeyVaultSecretGateway> _gateway = new();

    public PaymentKeyRingStoreTests()
    {
        _gateway.Setup(x => x.TryReadAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(KeyVaultSecretRead.NotFound);
        _gateway.Setup(x => x.TrySetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task A_scope_without_a_ring_gets_one()
    {
        var outcome = await Store().TryCreateAsync(Scope, CancellationToken.None);

        outcome.Should().Be(KeyRingProvisionOutcome.Created);

        var written = WrittenRing();
        written.ActiveKeyId.Should().NotBeNullOrWhiteSpace();
        written.Keys.Should().ContainKey(written.ActiveKeyId);
        Convert.FromBase64String(written.Keys[written.ActiveKeyId])
            .Should().HaveCount(32, "AES-256 keys are 32 bytes");
    }

    /// <summary>
    /// The rule the whole class exists for. Replacing an existing ring's active key makes
    /// every credential and stored card encrypted under the old one permanently unreadable,
    /// so an existing ring is never written to — not even to add a key.
    /// </summary>
    [Fact]
    public async Task An_existing_ring_is_never_written_to()
    {
        _gateway.Setup(x => x.TryReadAsync(
                PaymentKeyRingSecretName.Create(Scope),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(KeyVaultSecretRead.Found("{\"activeKeyId\":\"k\",\"keys\":{\"k\":\"" + SharedKey + "\"}}"));

        var outcome = await Store().TryCreateAsync(Scope, CancellationToken.None);

        outcome.Should().Be(KeyRingProvisionOutcome.AlreadyExists);
        _gateway.Verify(
            x => x.TrySetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// "Could not ask" must not be read as "not there". A vault that is refusing us — a
    /// missing grant, a throttle — would otherwise authorise a write over a ring that exists.
    /// </summary>
    [Fact]
    public async Task An_unreadable_vault_is_not_treated_as_an_absent_ring()
    {
        _gateway.Setup(x => x.TryReadAsync(
                PaymentKeyRingSecretName.Create(Scope),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(KeyVaultSecretRead.Unavailable);

        var outcome = await Store().TryCreateAsync(Scope, CancellationToken.None);

        outcome.Should().Be(KeyRingProvisionOutcome.Unavailable);
        _gateway.Verify(
            x => x.TrySetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_failed_write_reports_unavailable()
    {
        _gateway.Setup(x => x.TrySetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var outcome = await Store().TryCreateAsync(Scope, CancellationToken.None);

        outcome.Should().Be(KeyRingProvisionOutcome.Unavailable);
    }

    /// <summary>
    /// While the shared fallback is on, an unprovisioned scope has been writing under the
    /// shared key. Giving it a ring of its own stops that fallback, so the shared keys have to
    /// come with it or everything it already wrote becomes unreadable the moment it is
    /// provisioned.
    /// </summary>
    [Fact]
    public async Task A_new_ring_carries_the_shared_keys_so_existing_records_still_open()
    {
        GivenSharedRing("shared-key-2026-01");

        var outcome = await Store().TryCreateAsync(Scope, CancellationToken.None);

        outcome.Should().Be(KeyRingProvisionOutcome.Created);

        var written = WrittenRing();
        written.Keys.Should().ContainKey("shared-key-2026-01");
        written.Keys["shared-key-2026-01"].Should().Be(SharedKey);
    }

    /// <summary>
    /// Carried, but not active: new writes must land on the fresh key, or provisioning would
    /// achieve nothing and every organization would keep sharing one key.
    /// </summary>
    [Fact]
    public async Task The_seeded_shared_key_is_not_the_active_one()
    {
        GivenSharedRing("shared-key-2026-01");

        await Store().TryCreateAsync(Scope, CancellationToken.None);

        var written = WrittenRing();
        written.ActiveKeyId.Should().NotBe("shared-key-2026-01");
        written.Keys.Should().HaveCount(2);
    }

    [Fact]
    public async Task Nothing_is_seeded_once_the_shared_fallback_is_switched_off()
    {
        GivenSharedRing("shared-key-2026-01");

        await Store(fallBackToShared: false)
            .TryCreateAsync(Scope, CancellationToken.None);

        var written = WrittenRing();
        written.Keys.Should().HaveCount(1);
        written.Keys.Should().NotContainKey("shared-key-2026-01");
    }

    /// <summary>
    /// A malformed shared ring is already broken; refusing the new scope a key of its own
    /// would spread that failure rather than contain it.
    /// </summary>
    [Fact]
    public async Task A_malformed_shared_ring_does_not_block_provisioning()
    {
        _gateway.Setup(x => x.TryReadAsync(
                PaymentKeyRingSecretName.SharedSecretName,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(KeyVaultSecretRead.Found("not json"));

        var outcome = await Store().TryCreateAsync(Scope, CancellationToken.None);

        outcome.Should().Be(KeyRingProvisionOutcome.Created);
        WrittenRing().Keys.Should().HaveCount(1);
    }

    [Fact]
    public async Task The_ring_is_written_under_the_computed_secret_name()
    {
        await Store().TryCreateAsync(Scope, CancellationToken.None);

        _gateway.Verify(
            x => x.TrySetAsync(
                PaymentKeyRingSecretName.Create(Scope),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void GivenSharedRing(string keyId)
    {
        _gateway.Setup(x => x.TryReadAsync(
                PaymentKeyRingSecretName.SharedSecretName,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(KeyVaultSecretRead.Found(
                JsonSerializer.Serialize(new
                {
                    activeKeyId = keyId,
                    keys = new Dictionary<string, string> { [keyId] = SharedKey }
                })));
    }

    private ProviderTokenEncryptionKeyRingSecret WrittenRing()
    {
        string? payload = null;

        _gateway.Verify(
            x => x.TrySetAsync(
                It.IsAny<string>(),
                It.Is<string>(value => Capture(value, out payload)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        return JsonSerializer.Deserialize<ProviderTokenEncryptionKeyRingSecret>(
            payload!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static bool Capture(string value, out string? captured)
    {
        captured = value;
        return true;
    }

    private PaymentKeyRingStore Store(bool fallBackToShared = true)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(x => x.CurrentValue)
            .Returns(new PaymentOptions
            {
                FallBackToSharedEncryptionKeyRing = fallBackToShared
            });

        return new PaymentKeyRingStore(
            _gateway.Object,
            options.Object,
            NullLogger<PaymentKeyRingStore>.Instance);
    }
}
