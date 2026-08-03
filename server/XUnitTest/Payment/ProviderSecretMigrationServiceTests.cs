using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class ProviderSecretMigrationServiceTests
{
    private const string TenantId = "tenant-1";
    private const string KeyId = "key-1";
    private const string CredentialSecretName = "payment-adyen-shared";
    private const string TenantSecretName = "payment-tenant-1";

    private const string CredentialJson =
        "{\"apiKey\":\"adyen-key\",\"standardWebhookHmac\":{\"active\":\"aa\"}}";
    private const string TenantJson =
        "{\"returnStateHmac\":{\"active\":\"rr\"},\"shopperReferenceHmacKey\":\"ss\"}";

    private readonly Mock<IPaymentRepository> _repository = new();
    private readonly Mock<IVault> _vault = new();
    private readonly AesGcmSecretProtector _protector = new(
        new FixedKeyRingProvider(
            new ProviderTokenEncryptionKeyRing(
            KeyId,
            new Dictionary<string, byte[]>
            {
                [KeyId] = Enumerable.Repeat((byte)7, 32).ToArray()
            })));

    public ProviderSecretMigrationServiceTests()
    {
        _vault.Setup(x => x.ProcessSecretsAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [CredentialSecretName] = CredentialJson,
                [TenantSecretName] = TenantJson
            });
        _repository.Setup(x => x.SaveProviderSecretsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task Vault_json_is_carried_across_byte_for_byte()
    {
        string? credentialCiphertext = null;
        string? tenantCiphertext = null;

        ArrangeProviders(VaultBackedProvider());
        _repository.Setup(x => x.SaveProviderSecretsAsync(
                TenantId, "provider-1", It.IsAny<string>(),
                It.IsAny<string>(), KeyId, It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, CancellationToken>(
                (_, _, credential, tenant, _, _) =>
                {
                    credentialCiphertext = credential;
                    tenantCiphertext = tenant;
                })
            .ReturnsAsync(true);

        await Service().MigrateAsync(TenantId, CancellationToken.None);

        // The shopper reference key derives every stored payment method lookup, so the
        // migrated bytes must be identical, not merely equivalent.
        var scope = new PaymentEncryptionScope(TenantId, null);
        var credential = await _protector.UnprotectAsync(
            scope, credentialCiphertext!, KeyId);
        var tenant = await _protector.UnprotectAsync(
            scope, tenantCiphertext!, KeyId);

        credential.IsRead.Should().BeTrue();
        tenant.IsRead.Should().BeTrue();
        credential.Plaintext.Should().Be(CredentialJson);
        tenant.Plaintext.Should().Be(TenantJson);
    }

    [Fact]
    public async Task A_vault_backed_provider_is_migrated()
    {
        ArrangeProviders(VaultBackedProvider());

        var summary = await Service().MigrateAsync(TenantId, CancellationToken.None);

        summary.Migrated.Should().Be(1);
        summary.Skipped.Should().Be(0);
        summary.Failed.Should().Be(0);
    }

    [Fact]
    public async Task An_already_encrypted_provider_is_left_alone()
    {
        var provider = VaultBackedProvider();
        provider.ProviderSecretsCiphertext = "already-there";
        ArrangeProviders(provider);

        var summary = await Service().MigrateAsync(TenantId, CancellationToken.None);

        summary.Skipped.Should().Be(1);
        summary.Migrated.Should().Be(0);
        _repository.Verify(x => x.SaveProviderSecretsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_provider_with_no_vault_pointers_is_left_alone()
    {
        ArrangeProviders(new PaymentProvider
        {
            ItemId = "provider-1",
            TenantId = TenantId,
            ProviderName = PaymentConstants.StripeProvider
        });

        (await Service().MigrateAsync(TenantId, CancellationToken.None))
            .Skipped.Should().Be(1);
    }

    [Fact]
    public async Task A_missing_vault_secret_is_reported_as_a_failure()
    {
        ArrangeProviders(VaultBackedProvider());
        _vault.Setup(x => x.ProcessSecretsAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(new Dictionary<string, string>());

        var summary = await Service().MigrateAsync(TenantId, CancellationToken.None);

        summary.Failed.Should().Be(1);
        summary.Migrated.Should().Be(0);
    }

    [Fact]
    public async Task A_vault_error_fails_that_provider_without_stopping_the_run()
    {
        ArrangeProviders(VaultBackedProvider(), VaultBackedProvider("provider-2"));
        _vault.SetupSequence(x => x.ProcessSecretsAsync(It.IsAny<List<string>>()))
            .ThrowsAsync(new InvalidOperationException("vault down"))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [CredentialSecretName] = CredentialJson,
                [TenantSecretName] = TenantJson
            });

        var summary = await Service().MigrateAsync(TenantId, CancellationToken.None);

        summary.Failed.Should().Be(1);
        summary.Migrated.Should().Be(1);
    }

    [Fact]
    public async Task A_write_that_loses_the_race_is_not_counted_as_migrated()
    {
        ArrangeProviders(VaultBackedProvider());
        _repository.Setup(x => x.SaveProviderSecretsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        (await Service().MigrateAsync(TenantId, CancellationToken.None))
            .Failed.Should().Be(1);
    }

    private void ArrangeProviders(params PaymentProvider[] providers) =>
        _repository.Setup(x => x.GetProvidersAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(providers);

    private static PaymentProvider VaultBackedProvider(string itemId = "provider-1") => new()
    {
        ItemId = itemId,
        TenantId = TenantId,
        ProviderName = PaymentConstants.AdyenOnlineProvider,
        ProviderCredentialSecretName = CredentialSecretName,
        TenantSecuritySecretName = TenantSecretName
    };

    private ProviderSecretMigrationService Service() => new(
        _repository.Object,
        Mock.Of<IPaymentTenantContextScopeFactory>(x =>
            x.Establish(It.IsAny<string>()) == Mock.Of<IDisposable>()),
        _protector,
        _vault.Object,
        NullLogger<ProviderSecretMigrationService>.Instance);
}
