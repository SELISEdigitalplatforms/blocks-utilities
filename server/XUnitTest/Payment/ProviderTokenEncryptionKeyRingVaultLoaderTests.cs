using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Moq;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class ProviderTokenEncryptionKeyRingVaultLoaderTests
{
    [Fact]
    public async Task Loads_versioned_keyring_from_organization_vault()
    {
        var activeKey =
            Enumerable.Range(1, 32)
                .Select(value => (byte)value)
                .ToArray();
        var previousKey =
            Enumerable.Range(33, 32)
                .Select(value => (byte)value)
                .ToArray();
        var serialized = JsonSerializer.Serialize(
            new ProviderTokenEncryptionKeyRingSecret
            {
                ActiveKeyId = "key-2",
                Keys =
                    new Dictionary<string, string>
                    {
                        ["key-1"] =
                            Convert.ToBase64String(previousKey),
                        ["key-2"] =
                            Convert.ToBase64String(activeKey)
                    }
            });
        var vault = CreateVault(serialized);

        using var keyRing =
            await ProviderTokenEncryptionKeyRingVaultLoader
                .LoadAsync(vault.Object);

        keyRing.ActiveKeyId.Should().Be("key-2");
        keyRing.TryGetKey("key-1", out var loadedPrevious)
            .Should()
            .BeTrue();
        keyRing.TryGetKey("key-2", out var loadedActive)
            .Should()
            .BeTrue();
        loadedPrevious.Should().Equal(previousKey);
        loadedActive.Should().Equal(activeKey);

        vault.Verify(
            value => value.ProcessSecretsAsync(
                It.Is<List<string>>(
                    names =>
                        names.SequenceEqual(
                            new[]
                            {
                                ProviderTokenEncryptionKeyRingVaultLoader
                                    .SecretName
                            }))),
            Times.Once);
    }

    [Fact]
    public async Task Rejects_keyring_without_active_key()
    {
        var serialized = JsonSerializer.Serialize(
            new ProviderTokenEncryptionKeyRingSecret
            {
                ActiveKeyId = "key-2",
                Keys =
                    new Dictionary<string, string>
                    {
                        ["key-1"] =
                            Convert.ToBase64String(
                                new byte[32])
                    }
            });
        var vault = CreateVault(serialized);

        var action = async () =>
            await ProviderTokenEncryptionKeyRingVaultLoader
                .LoadAsync(vault.Object);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "*active provider-token encryption key is missing*");
    }

    [Fact]
    public async Task Rejects_invalid_encryption_key_length()
    {
        var serialized = JsonSerializer.Serialize(
            new ProviderTokenEncryptionKeyRingSecret
            {
                ActiveKeyId = "key-1",
                Keys =
                    new Dictionary<string, string>
                    {
                        ["key-1"] =
                            Convert.ToBase64String(
                                new byte[15])
                    }
            });
        var vault = CreateVault(serialized);

        var action = async () =>
            await ProviderTokenEncryptionKeyRingVaultLoader
                .LoadAsync(vault.Object);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*contains an invalid key*");
    }

    [Fact]
    public async Task Safe_load_keeps_host_available_when_secret_is_missing()
    {
        var vault = new Mock<IVault>();
        vault.Setup(
                value => value.ProcessSecretsAsync(
                    It.IsAny<List<string>>()))
            .ReturnsAsync(new Dictionary<string, string>());

        var result =
            await ProviderTokenEncryptionKeyRingVaultLoader
                .LoadSafelyAsync(vault.Object);
        using var keyRing = result.KeyRing;

        result.Readiness.IsProviderTokenEncryptionAvailable
            .Should()
            .BeFalse();
        result.Readiness.FailureCode.Should()
            .Be("provider_token_encryption_keyring_unavailable");
        keyRing.ActiveKeyId.Should().BeEmpty();
        keyRing.TryGetKey("missing", out var key)
            .Should()
            .BeFalse();
        key.Should().BeEmpty();
    }

    [Fact]
    public async Task Safe_load_keeps_host_available_when_vault_fails()
    {
        var vault = new Mock<IVault>();
        vault.Setup(
                value => value.ProcessSecretsAsync(
                    It.IsAny<List<string>>()))
            .ThrowsAsync(new InvalidOperationException("vault unavailable"));

        var result =
            await ProviderTokenEncryptionKeyRingVaultLoader
                .LoadSafelyAsync(vault.Object);
        using var keyRing = result.KeyRing;

        result.Readiness.IsProviderTokenEncryptionAvailable
            .Should()
            .BeFalse();
        keyRing.TryGetKey("missing", out _)
            .Should()
            .BeFalse();
    }

    private static Mock<IVault> CreateVault(
        string serializedKeyRing)
    {
        var vault = new Mock<IVault>();
        vault.Setup(
                value => value.ProcessSecretsAsync(
                    It.IsAny<List<string>>()))
            .ReturnsAsync(
                new Dictionary<string, string>
                {
                    [
                        ProviderTokenEncryptionKeyRingVaultLoader
                            .SecretName
                    ] = serializedKeyRing
                });

        return vault;
    }
}
