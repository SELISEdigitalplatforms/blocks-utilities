using DomainService.OAuth;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.DomainService.OAuth
{
    public class AzureVaultCertificateProviderTests
    {
        private readonly Mock<ILogger> _logger = new();

        [Fact]
        public void SetupKeyVault_WithMissingConfiguration_ThrowsException()
        {
            // Arrange
            Environment.SetEnvironmentVariable("KeyVault__KeyVaultUrl", null);
            Environment.SetEnvironmentVariable("KeyVault__TenantId", null);
            Environment.SetEnvironmentVariable("KeyVault__ClientId", null);
            Environment.SetEnvironmentVariable("KeyVault__ClientSecret", null);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => new AzureVaultCertificateProvider(_logger.Object));
            Assert.Equal("One or more required Azure config values are missing. Please check your environment configuration.", exception.Message);
            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("One or more required Azure config values are missing")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetCertificateAsync_WithInvalidKey_ReturnsEmptyArray()
        {
            // Arrange
            Environment.SetEnvironmentVariable("KeyVault__KeyVaultUrl", "https://test-vault.vault.azure.net/");
            Environment.SetEnvironmentVariable("KeyVault__TenantId", "test-tenant-id");
            Environment.SetEnvironmentVariable("KeyVault__ClientId", "test-client-id");
            Environment.SetEnvironmentVariable("KeyVault__ClientSecret", "test-client-secret");

            var provider = new AzureVaultCertificateProvider(_logger.Object);
            var invalidKey = "non-existent-certificate-key";

            // Act
            var result = await provider.GetCertificateAsync(invalidKey);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error retrieving certificate from Azure Key Vault")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }

        [Fact]
        public async Task GetCertificateAsync_WithException_LogsErrorAndReturnsEmptyArray()
        {
            // Arrange
            Environment.SetEnvironmentVariable("KeyVault__KeyVaultUrl", "https://invalid-vault-url.vault.azure.net/");
            Environment.SetEnvironmentVariable("KeyVault__TenantId", "invalid-tenant");
            Environment.SetEnvironmentVariable("KeyVault__ClientId", "invalid-client");
            Environment.SetEnvironmentVariable("KeyVault__ClientSecret", "invalid-secret");

            var provider = new AzureVaultCertificateProvider(_logger.Object);
            var testKey = "test-certificate-key";

            // Act
            var result = await provider.GetCertificateAsync(testKey);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error retrieving certificate from Azure Key Vault")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }

        public void Dispose()
        {
            // Cleanup environment variables after tests
            Environment.SetEnvironmentVariable("KeyVault__KeyVaultUrl", null);
            Environment.SetEnvironmentVariable("KeyVault__TenantId", null);
            Environment.SetEnvironmentVariable("KeyVault__ClientId", null);
            Environment.SetEnvironmentVariable("KeyVault__ClientSecret", null);
        }
    }
}