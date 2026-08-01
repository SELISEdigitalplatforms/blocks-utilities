using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class ProviderTokenProtectorTests
{
    private static readonly PaymentEncryptionScope Scope =
        new("tenant", "organization");

    [Fact]
    public async Task Protected_token_round_trips_without_plaintext_storage()
    {
        const string providerToken =
            "8415995487234100";
        var protector = CreateProtector();

        var protection = await protector.ProtectAsync(
            Scope,
            providerToken);

        protection.IsProtected.Should().BeTrue();

        var protectedToken = protection.Token!;

        protectedToken.Ciphertext.Should()
            .NotContain(providerToken);
        protectedToken.Fingerprint.Should()
            .NotBe(providerToken);

        var method = new StoredPaymentMethod
        {
            TenantId = Scope.TenantId,
            OrganizationId = Scope.OrganizationId,
            ProviderTokenCiphertext =
                protectedToken.Ciphertext,
            ProviderTokenFingerprint =
                protectedToken.Fingerprint,
            TokenEncryptionKeyId =
                protectedToken.EncryptionKeyId
        };

        var recovered = await protector.UnprotectAsync(method);

        recovered.IsRead.Should().BeTrue();
        recovered.ProviderToken.Should().Be(providerToken);
    }

    [Fact]
    public async Task Token_protection_rejects_missing_key_configuration()
    {
        var keyRing =
            new Mock<IProviderTokenEncryptionKeyRing>();
        keyRing.SetupGet(value => value.ActiveKeyId)
            .Returns("missing-key");

        byte[] ignored = [];
        keyRing.Setup(
                value => value.TryGetKey(
                    "missing-key",
                    out ignored))
            .Returns(false);

        var protector =
            new ProviderTokenProtector(
                new AesGcmSecretProtector(
                    new FixedKeyRingProvider(keyRing.Object)));

        var protection = await protector.ProtectAsync(Scope, "token");

        protection.IsProtected.Should().BeFalse();
    }

    /// <summary>
    /// A card belonging to one organization must not open under another's key ring, which is
    /// the whole point of scoping the rings. Decryption resolves the ring from the record, so
    /// a record naming a different organization reaches a different ring and fails to open.
    /// </summary>
    [Fact]
    public async Task A_card_does_not_open_under_another_organizations_key_ring()
    {
        using var firstRing = KeyRing("key-1", 1);
        using var secondRing = KeyRing("key-1", 200);

        var protector = new ProviderTokenProtector(
            new AesGcmSecretProtector(
                new ScopedKeyRingProvider(
                    new Dictionary<string, IProviderTokenEncryptionKeyRing>
                    {
                        ["tenant/organization-1"] = firstRing,
                        ["tenant/organization-2"] = secondRing
                    })));

        var protection = await protector.ProtectAsync(
            new PaymentEncryptionScope("tenant", "organization-1"),
            "provider-token");

        protection.IsProtected.Should().BeTrue();

        // Same ciphertext, same key id — only the owning organization differs.
        var stolen = new StoredPaymentMethod
        {
            TenantId = "tenant",
            OrganizationId = "organization-2",
            ProviderTokenCiphertext = protection.Token!.Ciphertext,
            TokenEncryptionKeyId = protection.Token.EncryptionKeyId
        };

        var recovered = await protector.UnprotectAsync(stolen);

        recovered.IsRead.Should().BeFalse();
    }

    [Fact]
    public void Fingerprint_is_deterministic_without_exposing_token()
    {
        var protector = CreateProtector();

        var first =
            protector.CreateFingerprint("provider-token");
        var second =
            protector.CreateFingerprint("provider-token");

        first.Should().Be(second);
        first.Should().NotContain("provider-token");
    }

    [Fact]
    public async Task Previous_key_remains_available_after_rotation()
    {
        var previousKey =
            Enumerable.Range(1, 32)
                .Select(value => (byte)value)
                .ToArray();
        var currentKey =
            Enumerable.Range(33, 32)
                .Select(value => (byte)value)
                .ToArray();
        using var previousKeyRing =
            new ProviderTokenEncryptionKeyRing(
                "key-1",
                new Dictionary<string, byte[]>
                {
                    ["key-1"] = previousKey
                });
        var previousProtector =
            new ProviderTokenProtector(
                new AesGcmSecretProtector(
                    new FixedKeyRingProvider(previousKeyRing)));

        var protection = await previousProtector.ProtectAsync(
            Scope,
            "provider-token");

        protection.IsProtected.Should().BeTrue();

        using var rotatedKeyRing =
            new ProviderTokenEncryptionKeyRing(
                "key-2",
                new Dictionary<string, byte[]>
                {
                    ["key-1"] = previousKey,
                    ["key-2"] = currentKey
                });
        var rotatedProtector =
            new ProviderTokenProtector(
                new AesGcmSecretProtector(
                    new FixedKeyRingProvider(rotatedKeyRing)));
        var method =
            new StoredPaymentMethod
            {
                TenantId = Scope.TenantId,
                OrganizationId = Scope.OrganizationId,
                ProviderTokenCiphertext =
                    protection.Token!.Ciphertext,
                TokenEncryptionKeyId =
                    protection.Token.EncryptionKeyId
            };

        var recovered = await rotatedProtector.UnprotectAsync(method);

        recovered.IsRead.Should().BeTrue();
        recovered.ProviderToken.Should().Be("provider-token");
    }

    private static ProviderTokenEncryptionKeyRing KeyRing(
        string keyId,
        int seed) =>
        new(
            keyId,
            new Dictionary<string, byte[]>
            {
                [keyId] = Enumerable.Range(seed, 32)
                    .Select(value => (byte)value)
                    .ToArray()
            });

    private static ProviderTokenProtector CreateProtector() =>
        new(
            new AesGcmSecretProtector(
                new FixedKeyRingProvider(KeyRing("key-1", 1))));
}
