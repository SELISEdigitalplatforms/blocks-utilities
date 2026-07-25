using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class ProviderTokenProtectorBranchTests
{
    private static ProviderTokenEncryptionKeyRing KeyRing(byte fill = 7) =>
        new("key-1", new Dictionary<string, byte[]>
        {
            ["key-1"] = Enumerable.Repeat(fill, 32).ToArray()
        });

    private static ProviderTokenProtector Protector(byte fill = 7) =>
        new(KeyRing(fill));

    [Fact]
    public void Unprotect_returns_false_when_no_token_material_is_present()
    {
        var success = Protector().TryUnprotect(
            new StoredPaymentMethod(), out var token);

        success.Should().BeFalse();
        token.Should().BeEmpty();
    }

    [Fact]
    public void Unprotect_returns_legacy_plaintext_token_when_ciphertext_absent()
    {
        var success = Protector().TryUnprotect(
            new StoredPaymentMethod { StoredPaymentMethodToken = "legacy-token" },
            out var token);

        success.Should().BeTrue();
        token.Should().Be("legacy-token");
    }

    [Fact]
    public void Unprotect_returns_false_when_encryption_key_is_missing()
    {
        var success = Protector().TryUnprotect(
            new StoredPaymentMethod
            {
                ProviderTokenCiphertext = "AAAA",
                TokenEncryptionKeyId = null
            },
            out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void Unprotect_returns_false_when_payload_is_too_short()
    {
        var shortPayload = Convert.ToBase64String(new byte[10]);
        var success = Protector().TryUnprotect(
            new StoredPaymentMethod
            {
                ProviderTokenCiphertext = shortPayload,
                TokenEncryptionKeyId = "key-1"
            },
            out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void Unprotect_returns_false_when_ciphertext_is_not_valid_base64()
    {
        var success = Protector().TryUnprotect(
            new StoredPaymentMethod
            {
                ProviderTokenCiphertext = "!!!not-base64!!!",
                TokenEncryptionKeyId = "key-1"
            },
            out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void Unprotect_returns_false_when_key_material_does_not_match()
    {
        Protector(fill: 7).TryProtect("provider-token", out var protectedToken)
            .Should().BeTrue();
        var method = new StoredPaymentMethod
        {
            ProviderTokenCiphertext = protectedToken.Ciphertext,
            TokenEncryptionKeyId = protectedToken.EncryptionKeyId
        };

        // A protector whose "key-1" holds different bytes cannot authenticate the tag.
        var success = Protector(fill: 9).TryUnprotect(method, out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void Protect_then_unprotect_round_trips_the_token()
    {
        var protector = Protector();
        protector.TryProtect("provider-token", out var protectedToken)
            .Should().BeTrue();
        var method = new StoredPaymentMethod
        {
            ProviderTokenCiphertext = protectedToken.Ciphertext,
            TokenEncryptionKeyId = protectedToken.EncryptionKeyId
        };

        protector.TryUnprotect(method, out var token).Should().BeTrue();
        token.Should().Be("provider-token");
    }
}
