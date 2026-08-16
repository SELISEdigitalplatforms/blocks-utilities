using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Payment.DomainService.Validators;

namespace XUnitTest.Payment;

public sealed class PaymentProviderCredentialRotationTests
{
    [Fact]
    public async Task Rotation_requires_an_explicit_version()
    {
        var validator =
            new RotatePaymentProviderCredentialsRequestValidator();
        var request = new RotatePaymentProviderCredentialsRequest
        {
            ApiKey = "api-key"
        };

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(request.Version));
    }

    private const string TenantId = "tenant-1";
    private const string KeyId = "key-1";
    private const string OldAdyenHmac =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string NewAdyenHmac =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private static readonly string SecurityKey =
        Convert.ToBase64String(
            Enumerable.Repeat((byte)3, 32).ToArray());

    private readonly AesGcmSecretProtector _protector = new(
        new FixedKeyRingProvider(
            new ProviderTokenEncryptionKeyRing(
            KeyId,
            new Dictionary<string, byte[]>
            {
                [KeyId] = Enumerable.Repeat((byte)7, 32).ToArray()
            })));

    [Fact]
    public async Task Adyen_webhook_rotation_preserves_the_old_active_secret()
    {
        var provider = await ProviderAsync(
            PaymentConstants.AdyenOnlineProvider,
            new ProviderCredentialSecret
            {
                ApiKey = "adyen-api-key",
                StandardWebhookHmac =
                    new RotatingPaymentSecret
                    {
                        Active = OldAdyenHmac
                    },
                TokenWebhookHmac =
                    new RotatingPaymentSecret
                    {
                        Active = OldAdyenHmac
                    }
            });
        var strategy = new AdyenCredentialRotationStrategy(
            new ProviderSecretReader(_protector));

        var plan = await strategy.CreatePlanAsync(
            provider,
            new RotatePaymentProviderCredentialsRequest
            {
                WebhookHmacKey = NewAdyenHmac
            });

        plan.IsSuccess.Should().BeTrue();
        var rotated = JsonSerializer.Deserialize<
            ProviderCredentialSecret>(
                plan.CredentialJson,
                SerializerOptions())!;
        rotated.StandardWebhookHmac.Active
            .Should().Be(NewAdyenHmac);
        rotated.StandardWebhookHmac.Previous
            .Should().Be(OldAdyenHmac);
        rotated.TokenWebhookHmac.Active
            .Should().Be(OldAdyenHmac);

        // Rotating one credential must not disturb the others. Asserted here rather than left
        // implicit because the failure is silent: rotation still reports success, and the loss
        // only surfaces on the next call to the provider, as an authentication error nobody
        // connects to a webhook rotation done days earlier.
        rotated.ApiKey.Should().Be("adyen-api-key");
    }

    [Fact]
    public async Task Stripe_webhook_rotation_preserves_the_old_active_secret()
    {
        var provider = await ProviderAsync(
            PaymentConstants.StripeProvider,
            new StripeCredentialSecret
            {
                SecretKey = "sk_test_old",
                WebhookSigningSecret =
                    new RotatingPaymentSecret
                    {
                        Active = "whsec_old"
                    }
            });
        var strategy = new StripeCredentialRotationStrategy(
            new ProviderSecretReader(_protector));

        var plan = await strategy.CreatePlanAsync(
            provider,
            new RotatePaymentProviderCredentialsRequest
            {
                WebhookHmacKey = "whsec_new"
            });

        plan.IsSuccess.Should().BeTrue();
        var rotated = JsonSerializer.Deserialize<
            StripeCredentialSecret>(
                plan.CredentialJson,
                SerializerOptions())!;
        rotated.WebhookSigningSecret.Active
            .Should().Be("whsec_new");
        rotated.WebhookSigningSecret.Previous
            .Should().Be("whsec_old");

        // Rotating the webhook secret alone is the common case — the API key and the endpoint
        // secret are rotated on different schedules — and wiping the key here would leave every
        // later Stripe call unauthorized while the rotation itself reported success.
        rotated.SecretKey.Should().Be("sk_test_old");
    }

    [Fact]
    public async Task Malformed_Adyen_HMAC_is_rejected_before_encryption()
    {
        var provider = await ProviderAsync(
            PaymentConstants.AdyenOnlineProvider,
            new ProviderCredentialSecret
            {
                ApiKey = "adyen-api-key",
                StandardWebhookHmac =
                    new RotatingPaymentSecret
                    {
                        Active = OldAdyenHmac
                    },
                TokenWebhookHmac =
                    new RotatingPaymentSecret
                    {
                        Active = OldAdyenHmac
                    }
            });
        var strategy = new AdyenCredentialRotationStrategy(
            new ProviderSecretReader(_protector));

        var plan = await strategy.CreatePlanAsync(
            provider,
            new RotatePaymentProviderCredentialsRequest
            {
                WebhookHmacKey = "not-hex"
            });

        plan.IsSuccess.Should().BeFalse();
        plan.ErrorCode.Should().Be(
            "payment_provider_credentials_invalid");
    }

    [Fact]
    public async Task Rotation_atomically_writes_both_envelopes_and_refreshes_cache()
    {
        var current = await ProviderAsync(
            PaymentConstants.AdyenOnlineProvider,
            new ProviderCredentialSecret
            {
                ApiKey = "adyen-api-key",
                StandardWebhookHmac =
                    new RotatingPaymentSecret
                    {
                        Active = OldAdyenHmac
                    },
                TokenWebhookHmac =
                    new RotatingPaymentSecret
                    {
                        Active = OldAdyenHmac
                    }
            });
        current.Version = 3;

        var contextResolver =
            new Mock<IPaymentExecutionContextResolver>();
        contextResolver.Setup(resolver =>
                resolver.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(
                    TenantId,
                    "actor-1",
                    null),
                null));

        var repository = new Mock<IPaymentRepository>();
        repository.Setup(item => item.GetProviderByIdAsync(
                TenantId,
                current.ItemId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);

        string? storedProviderCiphertext = null;
        string? storedTenantCiphertext = null;
        string? storedKeyId = null;
        var updated = new PaymentProvider
        {
            ItemId = current.ItemId,
            TenantId = TenantId,
            ProviderName = current.ProviderName,
            MerchantId = current.MerchantId,
            ApiBaseUrl = current.ApiBaseUrl,
            Version = 4,
            IsEnabled = true
        };
        repository.Setup(item =>
                item.TryRotateProviderCredentialsAsync(
                    TenantId,
                    current.ItemId,
                    3,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
            .Callback<string, string, long, string, string, string,
                CancellationToken>(
                (_, _, _, providerCiphertext, tenantCiphertext,
                    keyId, _) =>
                {
                    storedProviderCiphertext = providerCiphertext;
                    storedTenantCiphertext = tenantCiphertext;
                    storedKeyId = keyId;
                })
            .ReturnsAsync(updated);

        var cache = new Mock<IPaymentProviderCache>();
        cache.Setup(item => item.RefreshAsync(
                TenantId,
                It.IsAny<string>(),
                current.ProviderName,
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(updated);

        var service = new PaymentProviderCredentialRotationService(
            contextResolver.Object,
            new RotatePaymentProviderCredentialsRequestValidator(),
            [
                new AdyenCredentialRotationStrategy(
                    new ProviderSecretReader(_protector))
            ],
            _protector,
            repository.Object,
            cache.Object,
            new PaymentProviderResponseMapper(),
            NullLogger<
                PaymentProviderCredentialRotationService>.Instance);

        var result = await service.RotateAsync(
            current.ItemId,
            new RotatePaymentProviderCredentialsRequest
            {
                Version = 3,
                WebhookHmacKey = NewAdyenHmac
            },
            "corr",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        storedProviderCiphertext.Should().NotBeNullOrWhiteSpace();
        storedTenantCiphertext.Should().NotBeNullOrWhiteSpace();
        storedKeyId.Should().Be(KeyId);
        cache.Verify(item => item.Remove(
            TenantId,
            It.IsAny<string>(),
            current.ProviderName), Times.Once);
        cache.Verify(item => item.RefreshAsync(
            TenantId,
            It.IsAny<string>(),
            current.ProviderName,
            It.IsAny<Func<Task<PaymentProvider?>>>()), Times.Once);
    }

    [Fact]
    public void Shopper_reference_identity_key_is_not_rotatable()
    {
        var request = JsonSerializer.Deserialize<
            RotatePaymentProviderCredentialsRequest>(
                """
                {
                  "version": 3,
                  "webhookHmacKey": "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
                  "shopperReferenceHmacKey": "unsafe"
                }
                """,
                SerializerOptions())!;

        var validation =
            new RotatePaymentProviderCredentialsRequestValidator()
                .Validate(request);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(error =>
            error.ErrorCode ==
            "payment_provider_shopper_identity_key_immutable");
    }

    private async Task<PaymentProvider> ProviderAsync(
        string providerName,
        object credentials)
    {
        var scope = new PaymentEncryptionScope(TenantId, null);
        var credential = await _protector.ProtectAsync(
            scope,
            JsonSerializer.Serialize(
                credentials,
                SerializerOptions()));
        var tenant = await _protector.ProtectAsync(
            scope,
            JsonSerializer.Serialize(
                new TenantPaymentSecuritySecret
                {
                    ReturnStateHmac =
                        new RotatingPaymentSecret
                        {
                            Active = SecurityKey
                        },
                    ShopperReferenceHmacKey = SecurityKey
                },
                SerializerOptions()));

        credential.IsProtected.Should().BeTrue();
        tenant.IsProtected.Should().BeTrue();

        return new PaymentProvider
        {
            ItemId = "provider-1",
            TenantId = TenantId,
            ProviderName = providerName,
            MerchantId = "merchant-1",
            ApiBaseUrl = "https://example.test",
            ProviderSecretsCiphertext = credential.Ciphertext,
            TenantSecuritySecretsCiphertext = tenant.Ciphertext,
            SecretsEncryptionKeyId = credential.KeyId,
            IsEnabled = true
        };
    }

    private static JsonSerializerOptions SerializerOptions() =>
        new(JsonSerializerDefaults.Web);
}
