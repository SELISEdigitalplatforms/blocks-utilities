using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentProviderSecretHydratorTests
{
    private const string KeyId = "key-1";
    private const string HexHmac =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly string Base64Key =
        Convert.ToBase64String(Enumerable.Repeat((byte)3, 32).ToArray());

    private readonly AesGcmSecretProtector _protector = new(
        new FixedKeyRingProvider(
            new ProviderTokenEncryptionKeyRing(
            KeyId,
            new Dictionary<string, byte[]>
            {
                [KeyId] = Enumerable.Repeat((byte)7, 32).ToArray()
            })));

    private readonly ProviderSecretReader _reader;

    public PaymentProviderSecretHydratorTests()
    {
        _reader = new ProviderSecretReader(_protector);
    }

    [Fact]
    public async Task Adyen_credentials_are_decrypted_onto_the_provider()
    {
        var provider = await ProviderAsync(PaymentConstants.AdyenOnlineProvider, AdyenCredentials());

        (await Adyen().HydrateAsync(provider, CancellationToken.None)).Should().BeTrue();

        provider.ApiKey.Should().Be("adyen-api-key");
        provider.StandardWebhookHmacKey.Should().Be(HexHmac);
        provider.TokenWebhookHmacKey.Should().Be(HexHmac);
        provider.ReturnStateHmacKey.Should().Be(Base64Key);
        provider.ShopperReferenceHmacKey.Should().Be(Base64Key);
    }

    [Fact]
    public async Task Stripe_credentials_are_decrypted_onto_the_provider()
    {
        var provider = await ProviderAsync(PaymentConstants.StripeProvider, StripeCredentials());

        (await Stripe().HydrateAsync(provider, CancellationToken.None)).Should().BeTrue();

        provider.ApiKey.Should().Be("sk_test_123");
        provider.StandardWebhookHmacKey.Should().Be("whsec_abc");
        provider.ReturnStateHmacKey.Should().Be(Base64Key);
        provider.ShopperReferenceHmacKey.Should().Be(Base64Key);
    }

    [Fact]
    public async Task A_provider_without_encrypted_secrets_fails_closed()
    {
        var provider = new PaymentProvider
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider
        };

        (await Adyen().HydrateAsync(provider, CancellationToken.None)).Should().BeFalse();
        provider.ApiKey.Should().BeEmpty();
    }

    [Fact]
    public async Task A_tampered_ciphertext_fails_closed_rather_than_decrypting()
    {
        var provider = await ProviderAsync(PaymentConstants.AdyenOnlineProvider, AdyenCredentials());
        var payload = Convert.FromBase64String(provider.ProviderSecretsCiphertext!);
        payload[^1] ^= 0xFF;
        provider.ProviderSecretsCiphertext = Convert.ToBase64String(payload);

        (await Adyen().HydrateAsync(provider, CancellationToken.None)).Should().BeFalse();
        provider.ApiKey.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_encryption_key_fails_closed()
    {
        var provider = await ProviderAsync(PaymentConstants.AdyenOnlineProvider, AdyenCredentials());
        provider.SecretsEncryptionKeyId = "retired-key";

        (await Adyen().HydrateAsync(provider, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Credentials_failing_their_schema_are_rejected()
    {
        var provider = await ProviderAsync(
            PaymentConstants.AdyenOnlineProvider,
            new ProviderCredentialSecret
            {
                ApiKey = "adyen-api-key",
                StandardWebhookHmac = new RotatingPaymentSecret { Active = "not-hex" },
                TokenWebhookHmac = new RotatingPaymentSecret { Active = HexHmac }
            });

        (await Adyen().HydrateAsync(provider, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public void Each_hydrator_claims_only_its_own_provider()
    {
        Adyen().Supports(PaymentConstants.AdyenOnlineProvider).Should().BeTrue();
        Adyen().Supports(PaymentConstants.StripeProvider).Should().BeFalse();
        Stripe().Supports(PaymentConstants.StripeProvider).Should().BeTrue();
        Stripe().Supports(PaymentConstants.AdyenOnlineProvider).Should().BeFalse();
    }

    private AdyenSecretHydrator Adyen() =>
        new(_reader, NullLogger<AdyenSecretHydrator>.Instance);

    private StripeSecretHydrator Stripe() =>
        new(_reader, NullLogger<StripeSecretHydrator>.Instance);

    private async Task<PaymentProvider> ProviderAsync(string providerName, object credentials)
    {
        var scope = new PaymentEncryptionScope("tenant", null);
        var credential = await _protector.ProtectAsync(scope, Serialize(credentials));
        var security = await _protector.ProtectAsync(scope, Serialize(TenantSecurity()));

        credential.IsProtected.Should().BeTrue();
        security.IsProtected.Should().BeTrue();

        return new PaymentProvider
        {
            ProviderName = providerName,
            TenantId = "tenant",
            ProviderSecretsCiphertext = credential.Ciphertext,
            TenantSecuritySecretsCiphertext = security.Ciphertext,
            SecretsEncryptionKeyId = credential.KeyId
        };
    }

    private static string Serialize(object value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static ProviderCredentialSecret AdyenCredentials() => new()
    {
        ApiKey = "adyen-api-key",
        StandardWebhookHmac = new RotatingPaymentSecret { Active = HexHmac },
        TokenWebhookHmac = new RotatingPaymentSecret { Active = HexHmac }
    };

    private static StripeCredentialSecret StripeCredentials() => new()
    {
        SecretKey = "sk_test_123",
        WebhookSigningSecret = new RotatingPaymentSecret { Active = "whsec_abc" }
    };

    private static TenantPaymentSecuritySecret TenantSecurity() => new()
    {
        ReturnStateHmac = new RotatingPaymentSecret { Active = Base64Key },
        ShopperReferenceHmacKey = Base64Key
    };
}
