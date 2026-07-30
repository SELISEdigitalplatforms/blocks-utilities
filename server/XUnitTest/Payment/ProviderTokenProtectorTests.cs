using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class ProviderTokenProtectorTests
{
    [Fact]
    public void Protected_token_round_trips_without_plaintext_storage()
    {
        const string providerToken =
            "8415995487234100";
        var protector = CreateProtector();

        protector.TryProtect(
                providerToken,
                out var protectedToken)
            .Should()
            .BeTrue();

        protectedToken.Ciphertext.Should()
            .NotContain(providerToken);
        protectedToken.Fingerprint.Should()
            .NotBe(providerToken);

        var method = new StoredPaymentMethod
        {
            ProviderTokenCiphertext =
                protectedToken.Ciphertext,
            ProviderTokenFingerprint =
                protectedToken.Fingerprint,
            TokenEncryptionKeyId =
                protectedToken.EncryptionKeyId
        };

        protector.TryUnprotect(
                method,
                out var recoveredToken)
            .Should()
            .BeTrue();
        recoveredToken.Should().Be(providerToken);
    }

    [Fact]
    public void Token_protection_rejects_missing_key_configuration()
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
            new ProviderTokenProtector(new AesGcmSecretProtector(keyRing.Object));

        protector.TryProtect("token", out _)
            .Should()
            .BeFalse();
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
    public void Previous_key_remains_available_after_rotation()
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
            new ProviderTokenProtector(new AesGcmSecretProtector(previousKeyRing));

        previousProtector.TryProtect(
                "provider-token",
                out var protectedToken)
            .Should()
            .BeTrue();

        using var rotatedKeyRing =
            new ProviderTokenEncryptionKeyRing(
                "key-2",
                new Dictionary<string, byte[]>
                {
                    ["key-1"] = previousKey,
                    ["key-2"] = currentKey
                });
        var rotatedProtector =
            new ProviderTokenProtector(new AesGcmSecretProtector(rotatedKeyRing));
        var method =
            new StoredPaymentMethod
            {
                ProviderTokenCiphertext =
                    protectedToken.Ciphertext,
                TokenEncryptionKeyId =
                    protectedToken.EncryptionKeyId
            };

        rotatedProtector.TryUnprotect(
                method,
                out var recoveredToken)
            .Should()
            .BeTrue();
        recoveredToken.Should().Be("provider-token");
    }

    private static ProviderTokenProtector CreateProtector()
    {
        var keyRing =
            new ProviderTokenEncryptionKeyRing(
                "key-1",
                new Dictionary<string, byte[]>
                {
                    ["key-1"] =
                        Enumerable.Range(1, 32)
                            .Select(value => (byte)value)
                            .ToArray()
                });

        return new ProviderTokenProtector(new AesGcmSecretProtector(keyRing));
    }
}
