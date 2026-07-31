using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentSecretReEncryptionServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OldKeyId = "key-old";
    private const string NewKeyId = "key-new";

    private static readonly PaymentEncryptionScope Scope =
        new(TenantId, "organization-1");

    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IStoredPaymentMethodRepository> _methods = new();
    private readonly AesGcmSecretProtector _protector;
    private readonly ProviderTokenProtector _tokenProtector;
    private readonly IProviderTokenEncryptionKeyRingProvider _keyRings;

    public PaymentSecretReEncryptionServiceTests()
    {
        // Both keys present: the job decrypts under the old one and encrypts under the new.
        _keyRings = new FixedKeyRingProvider(
            new ProviderTokenEncryptionKeyRing(
                NewKeyId,
                new Dictionary<string, byte[]>
                {
                    [OldKeyId] = Enumerable.Repeat((byte)7, 32).ToArray(),
                    [NewKeyId] = Enumerable.Repeat((byte)9, 32).ToArray()
                }));
        _protector = new AesGcmSecretProtector(_keyRings);
        _tokenProtector = new ProviderTokenProtector(_protector);

        _payments.Setup(value => value.GetProvidersAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _methods.Setup(value => value.ListForReEncryptionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task A_provider_on_an_old_key_is_moved_onto_the_active_key()
    {
        var provider = await ProviderAsync();
        _payments.Setup(value => value.GetProvidersAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([provider]);

        string? writtenKeyId = null;
        string? writtenCiphertext = null;
        _payments.Setup(value => value.ReplaceProviderSecretsAsync(
                TenantId, provider.ItemId, OldKeyId,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, string, CancellationToken>(
                (_, _, _, credential, _, keyId, _) =>
                {
                    writtenCiphertext = credential;
                    writtenKeyId = keyId;
                })
            .ReturnsAsync(true);

        var summary = await Service().ReEncryptAsync(Scope, CancellationToken.None);

        summary.ProvidersReEncrypted.Should().Be(1);
        summary.Failed.Should().Be(0);
        writtenKeyId.Should().Be(NewKeyId);

        // The ciphertext changed, but what it decrypts to did not.
        writtenCiphertext.Should().NotBe(provider.ProviderSecretsCiphertext);
        var read = await _protector.UnprotectAsync(
            Scope, writtenCiphertext!, NewKeyId);
        read.Plaintext.Should().Be("{\"apiKey\":\"secret\"}");
    }

    /// <summary>
    /// The property that makes the job safe to re-run, and the one the whole migration relies
    /// on: nothing already on the active key is touched a second time.
    /// </summary>
    [Fact]
    public async Task A_second_run_over_an_unchanged_scope_writes_nothing()
    {
        var provider = await ProviderAsync();
        provider.SecretsEncryptionKeyId = NewKeyId;
        _payments.Setup(value => value.GetProvidersAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([provider]);

        var summary = await Service().ReEncryptAsync(Scope, CancellationToken.None);

        summary.ProvidersReEncrypted.Should().Be(0);
        summary.Skipped.Should().Be(1);
        _payments.Verify(value => value.ReplaceProviderSecretsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A provider rotated by someone else between the read and the write must not be clobbered
    /// with the value this run decrypted — the compare-and-set rejects it, and it is reported
    /// as skipped rather than re-encrypted.
    /// </summary>
    [Fact]
    public async Task A_provider_changed_mid_run_is_skipped_not_overwritten()
    {
        var provider = await ProviderAsync();
        _payments.Setup(value => value.GetProvidersAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([provider]);
        _payments.Setup(value => value.ReplaceProviderSecretsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var summary = await Service().ReEncryptAsync(Scope, CancellationToken.None);

        summary.ProvidersReEncrypted.Should().Be(0);
        summary.Skipped.Should().Be(1);
    }

    /// <summary>
    /// Another organization's providers live in the same tenant database. Re-encrypting one
    /// organization must not touch another's, whose key ring this run cannot even read.
    /// </summary>
    [Fact]
    public async Task Another_organizations_provider_is_left_alone()
    {
        var provider = await ProviderAsync();
        provider.OrganizationId = "organization-2";
        _payments.Setup(value => value.GetProvidersAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([provider]);

        var summary = await Service().ReEncryptAsync(Scope, CancellationToken.None);

        summary.ProvidersReEncrypted.Should().Be(0);
        summary.Skipped.Should().Be(0);
        _payments.Verify(value => value.ReplaceProviderSecretsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_provider_whose_key_is_gone_is_reported_as_failed()
    {
        var provider = await ProviderAsync();
        provider.SecretsEncryptionKeyId = "key-retired";
        _payments.Setup(value => value.GetProvidersAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([provider]);

        var summary = await Service().ReEncryptAsync(Scope, CancellationToken.None);

        summary.Failed.Should().Be(1);
        summary.ProvidersReEncrypted.Should().Be(0);
    }

    [Fact]
    public async Task A_saved_card_is_moved_onto_the_active_key()
    {
        var method = await MethodAsync("method-1");
        ArrangeMethodPage(method);

        ProtectedProviderToken? written = null;
        _methods.Setup(value => value.ReplaceProtectedTokenAsync(
                TenantId, "method-1", OldKeyId,
                It.IsAny<ProtectedProviderToken>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, ProtectedProviderToken, CancellationToken>(
                (_, _, _, token, _) => written = token)
            .ReturnsAsync(true);

        var summary = await Service().ReEncryptAsync(Scope, CancellationToken.None);

        summary.StoredPaymentMethodsReEncrypted.Should().Be(1);
        written!.EncryptionKeyId.Should().Be(NewKeyId);
    }

    /// <summary>
    /// The token fingerprint is a hash of the plaintext token, not of the ciphertext, so
    /// re-encryption must leave it untouched. Changing it would make every saved card look like
    /// a card the shopper does not have, and re-saving would silently duplicate it.
    /// </summary>
    [Fact]
    public async Task Re_encryption_preserves_the_token_fingerprint()
    {
        var method = await MethodAsync("method-1");
        var originalFingerprint = method.ProviderTokenFingerprint;
        ArrangeMethodPage(method);

        ProtectedProviderToken? written = null;
        _methods.Setup(value => value.ReplaceProtectedTokenAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ProtectedProviderToken>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, ProtectedProviderToken, CancellationToken>(
                (_, _, _, token, _) => written = token)
            .ReturnsAsync(true);

        await Service().ReEncryptAsync(Scope, CancellationToken.None);

        written!.Fingerprint.Should().Be(originalFingerprint);
    }

    [Fact]
    public async Task An_unreadable_key_ring_stops_before_writing_anything()
    {
        var provider = await ProviderAsync();
        _payments.Setup(value => value.GetProvidersAsync(
                TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([provider]);

        var service = new PaymentSecretReEncryptionService(
            _payments.Object,
            _methods.Object,
            new FixedKeyRingProvider(
                new UnavailableProviderTokenEncryptionKeyRing()),
            _protector,
            _tokenProtector,
            NullLogger<PaymentSecretReEncryptionService>.Instance);

        var summary = await service.ReEncryptAsync(Scope, CancellationToken.None);

        summary.Should().Be(
            new PaymentSecretReEncryptionSummary(0, 0, 0, 0));
        _payments.Verify(value => value.GetProvidersAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void ArrangeMethodPage(StoredPaymentMethod method)
    {
        _methods.SetupSequence(value => value.ListForReEncryptionAsync(
                TenantId, Scope.OrganizationId, NewKeyId,
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([method])
            .ReturnsAsync([]);
    }

    private PaymentSecretReEncryptionService Service() =>
        new(
            _payments.Object,
            _methods.Object,
            _keyRings,
            _protector,
            _tokenProtector,
            NullLogger<PaymentSecretReEncryptionService>.Instance);

    private async Task<PaymentProvider> ProviderAsync()
    {
        var credentials = await ProtectUnderOldKeyAsync("{\"apiKey\":\"secret\"}");
        var security = await ProtectUnderOldKeyAsync("{\"shopperReferenceHmacKey\":\"k\"}");

        return new PaymentProvider
        {
            ItemId = "provider-1",
            TenantId = TenantId,
            OrganizationId = Scope.OrganizationId,
            ProviderName = PaymentConstants.StripeProvider,
            ProviderSecretsCiphertext = credentials,
            TenantSecuritySecretsCiphertext = security,
            SecretsEncryptionKeyId = OldKeyId
        };
    }

    private async Task<StoredPaymentMethod> MethodAsync(string itemId)
    {
        var ciphertext = await ProtectUnderOldKeyAsync("pm_123");

        return new StoredPaymentMethod
        {
            ItemId = itemId,
            TenantId = TenantId,
            OrganizationId = Scope.OrganizationId,
            ProviderName = PaymentConstants.StripeProvider,
            ProviderTokenCiphertext = ciphertext,
            ProviderTokenFingerprint =
                _tokenProtector.CreateFingerprint("pm_123"),
            TokenEncryptionKeyId = OldKeyId
        };
    }

    /// <summary>
    /// Produces ciphertext under the retired key, which is what every record looks like before
    /// the job runs. The protector always writes under the active key, so this encrypts against
    /// a ring whose active key is the old one.
    /// </summary>
    private static async Task<string> ProtectUnderOldKeyAsync(string plaintext)
    {
        using var oldRing = new ProviderTokenEncryptionKeyRing(
            OldKeyId,
            new Dictionary<string, byte[]>
            {
                [OldKeyId] = Enumerable.Repeat((byte)7, 32).ToArray()
            });
        var protector = new AesGcmSecretProtector(
            new FixedKeyRingProvider(oldRing));
        var protection = await protector.ProtectAsync(Scope, plaintext);

        protection.IsProtected.Should().BeTrue();

        return protection.Ciphertext;
    }
}
