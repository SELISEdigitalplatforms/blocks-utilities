using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class ProviderTokenProtectorBranchTests
{
    private static readonly PaymentEncryptionScope Scope =
        new("tenant", null);

    private static ProviderTokenEncryptionKeyRing KeyRing(byte fill = 7) =>
        new("key-1", new Dictionary<string, byte[]>
        {
            ["key-1"] = Enumerable.Repeat(fill, 32).ToArray()
        });

    private static ProviderTokenProtector Protector(byte fill = 7) =>
        new(new AesGcmSecretProtector(new FixedKeyRingProvider(KeyRing(fill))));

    [Fact]
    public async Task Unprotect_returns_false_when_no_token_material_is_present()
    {
        var result = await Protector().UnprotectAsync(Method());

        result.IsRead.Should().BeFalse();
        result.ProviderToken.Should().BeEmpty();
    }

    [Fact]
    public async Task Unprotect_returns_legacy_plaintext_token_when_ciphertext_absent()
    {
        var method = Method();
        method.StoredPaymentMethodToken = "legacy-token";

        var result = await Protector().UnprotectAsync(method);

        result.IsRead.Should().BeTrue();
        result.ProviderToken.Should().Be("legacy-token");
    }

    [Fact]
    public async Task Unprotect_returns_false_when_encryption_key_is_missing()
    {
        var method = Method();
        method.ProviderTokenCiphertext = "AAAA";
        method.TokenEncryptionKeyId = null;

        var result = await Protector().UnprotectAsync(method);

        result.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task Unprotect_returns_false_when_payload_is_too_short()
    {
        var method = Method();
        method.ProviderTokenCiphertext = Convert.ToBase64String(new byte[10]);
        method.TokenEncryptionKeyId = "key-1";

        var result = await Protector().UnprotectAsync(method);

        result.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task Unprotect_returns_false_when_ciphertext_is_not_valid_base64()
    {
        var method = Method();
        method.ProviderTokenCiphertext = "!!!not-base64!!!";
        method.TokenEncryptionKeyId = "key-1";

        var result = await Protector().UnprotectAsync(method);

        result.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task Unprotect_returns_false_when_key_material_does_not_match()
    {
        var protection = await Protector(fill: 7)
            .ProtectAsync(Scope, "provider-token");

        protection.IsProtected.Should().BeTrue();

        var method = Method();
        method.ProviderTokenCiphertext = protection.Token!.Ciphertext;
        method.TokenEncryptionKeyId = protection.Token.EncryptionKeyId;

        // A protector whose "key-1" holds different bytes cannot authenticate the tag.
        var result = await Protector(fill: 9).UnprotectAsync(method);

        result.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task Protect_then_unprotect_round_trips_the_token()
    {
        var protector = Protector();
        var protection = await protector.ProtectAsync(Scope, "provider-token");

        protection.IsProtected.Should().BeTrue();

        var method = Method();
        method.ProviderTokenCiphertext = protection.Token!.Ciphertext;
        method.TokenEncryptionKeyId = protection.Token.EncryptionKeyId;

        var result = await protector.UnprotectAsync(method);

        result.IsRead.Should().BeTrue();
        result.ProviderToken.Should().Be("provider-token");
    }

    private static StoredPaymentMethod Method() =>
        new() { TenantId = Scope.TenantId };
}
