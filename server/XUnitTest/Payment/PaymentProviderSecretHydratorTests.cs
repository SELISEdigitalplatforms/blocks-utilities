using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MongoDB.Bson;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentProviderSecretHydratorTests
{
    private const string StandardActiveKey =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string StandardPreviousKey =
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string TokenActiveKey =
        "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
    private const string TokenPreviousKey =
        "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";

    private static readonly string ReturnActiveKey =
        Convert.ToBase64String(
            Enumerable.Repeat((byte)1, 32).ToArray());

    private static readonly string ReturnPreviousKey =
        Convert.ToBase64String(
            Enumerable.Repeat((byte)2, 32).ToArray());

    private static readonly string ShopperKey =
        Convert.ToBase64String(
            Enumerable.Repeat((byte)3, 32).ToArray());

    [Fact]
    public async Task Hydrates_runtime_secrets_from_both_vault_records()
    {
        var vault = CreateVault(
            Credentials(),
            TenantSecurity());
        var hydrator = CreateHydrator(vault.Object);
        var provider = Provider();

        var hydrated = await hydrator.HydrateAsync(
            provider,
            CancellationToken.None);

        hydrated.Should().BeTrue();
        provider.ApiKey.Should().Be("api-key");
        provider.StandardWebhookHmacKey
            .Should()
            .Be(StandardActiveKey);
        provider.PreviousStandardWebhookHmacKey
            .Should()
            .Be(StandardPreviousKey);
        provider.TokenWebhookHmacKey
            .Should()
            .Be(TokenActiveKey);
        provider.PreviousTokenWebhookHmacKey
            .Should()
            .Be(TokenPreviousKey);
        provider.ReturnStateHmacKey
            .Should()
            .Be(ReturnActiveKey);
        provider.PreviousReturnStateHmacKey
            .Should()
            .Be(ReturnPreviousKey);
        provider.ShopperReferenceHmacKey
            .Should()
            .Be(ShopperKey);

        vault.Verify(
            value => value.ProcessSecretsAsync(
                It.Is<List<string>>(
                    names =>
                        names.SequenceEqual(
                            new[]
                            {
                                "payment-provider-shared",
                                "payment-tenant-security"
                            }))),
            Times.Once);
    }

    [Fact]
    public async Task Fails_closed_when_a_secret_is_missing()
    {
        var vault = CreateVault(
            Credentials(),
            tenantSecurity: null);
        var provider = Provider();

        var hydrated = await CreateHydrator(vault.Object)
            .HydrateAsync(
                provider,
                CancellationToken.None);

        hydrated.Should().BeFalse();
        provider.ApiKey.Should().BeEmpty();
    }

    [Fact]
    public async Task Fails_closed_for_invalid_secret_reference()
    {
        var vault = new Mock<IVault>();
        var provider = Provider();
        provider.ProviderCredentialSecretName =
            "invalid/secret/name";

        var hydrated = await CreateHydrator(vault.Object)
            .HydrateAsync(
                provider,
                CancellationToken.None);

        hydrated.Should().BeFalse();
        vault.Verify(
            value => value.ProcessSecretsAsync(
                It.IsAny<List<string>>()),
            Times.Never);
    }

    [Fact]
    public void Runtime_secrets_are_not_serialized_to_mongodb()
    {
        var provider = Provider();
        provider.ApiKey = "api-key";
        provider.ReturnStateHmacKey = "return-key";
        provider.StandardWebhookHmacKey = "standard-key";
        provider.TokenWebhookHmacKey = "token-key";
        provider.ShopperReferenceHmacKey = "shopper-key";

        var document = provider.ToBsonDocument();

        document.Contains(nameof(PaymentProvider.ApiKey))
            .Should()
            .BeFalse();
        document.Contains(
                nameof(PaymentProvider.ReturnStateHmacKey))
            .Should()
            .BeFalse();
        document.Contains(
                nameof(PaymentProvider.StandardWebhookHmacKey))
            .Should()
            .BeFalse();
        document.Contains(
                nameof(PaymentProvider.TokenWebhookHmacKey))
            .Should()
            .BeFalse();
        document.Contains(
                nameof(PaymentProvider.ShopperReferenceHmacKey))
            .Should()
            .BeFalse();
        document[
                nameof(
                    PaymentProvider
                        .ProviderCredentialSecretName)]
            .AsString
            .Should()
            .Be("payment-provider-shared");
        document[
                nameof(
                    PaymentProvider
                        .TenantSecuritySecretName)]
            .AsString
            .Should()
            .Be("payment-tenant-security");
    }

    private static PaymentProviderSecretHydrator CreateHydrator(
        IVault vault) =>
        new(
            vault,
            NullLogger<PaymentProviderSecretHydrator>.Instance);

    private static Mock<IVault> CreateVault(
        ProviderCredentialSecret credentials,
        TenantPaymentSecuritySecret? tenantSecurity)
    {
        var secrets = new Dictionary<string, string>
        {
            ["payment-provider-shared"] =
                JsonSerializer.Serialize(credentials)
        };

        if (tenantSecurity != null)
        {
            secrets["payment-tenant-security"] =
                JsonSerializer.Serialize(tenantSecurity);
        }

        var vault = new Mock<IVault>();
        vault.Setup(
                value => value.ProcessSecretsAsync(
                    It.IsAny<List<string>>()))
            .ReturnsAsync(secrets);

        return vault;
    }

    private static ProviderCredentialSecret Credentials() =>
        new()
        {
            ApiKey = "api-key",
            StandardWebhookHmac =
                new RotatingPaymentSecret
                {
                    Active = StandardActiveKey,
                    Previous = StandardPreviousKey
                },
            TokenWebhookHmac =
                new RotatingPaymentSecret
                {
                    Active = TokenActiveKey,
                    Previous = TokenPreviousKey
                }
        };

    private static TenantPaymentSecuritySecret TenantSecurity() =>
        new()
        {
            ReturnStateHmac =
                new RotatingPaymentSecret
                {
                    Active = ReturnActiveKey,
                    Previous = ReturnPreviousKey
                },
            ShopperReferenceHmacKey = ShopperKey
        };

    private static PaymentProvider Provider() =>
        new()
        {
            ItemId = "provider-id",
            TenantId = "tenant-id",
            ProviderName = "ADYEN-ONLINE",
            ProviderCredentialSecretName =
                "payment-provider-shared",
            TenantSecuritySecretName =
                "payment-tenant-security"
        };
}
