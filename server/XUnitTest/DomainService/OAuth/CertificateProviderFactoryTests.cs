using Blocks.Genesis;
using DomainService.OAuth;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.DomainService.OAuth.CertificateProvider
{
    public class CertificateProviderFactoryTests
    {
        private readonly Mock<ILogger<CertificateProviderFactory>> _logger;
        private readonly Mock<IBlocksSecret> _blocksSecret;
        private readonly CertificateProviderFactory _factory;

        public CertificateProviderFactoryTests()
        {
            _logger = new Mock<ILogger<CertificateProviderFactory>>();
            _blocksSecret = new Mock<IBlocksSecret>();

            // Setup mock to return test values for MongoDB provider
            _blocksSecret.Setup(x => x.DatabaseConnectionString).Returns("mongodb://localhost:27017");
            _blocksSecret.Setup(x => x.RootDatabaseName).Returns("TestDatabase");

            _factory = new CertificateProviderFactory(_logger.Object, _blocksSecret.Object);
        }

        [Fact]
        public void GetProvider_WithFilefilesystemType_ReturnsFileSystemCertificateProvider()
        {
            // Act
            var result = _factory.GetProvider(CertificateStorageType.Filefilesystem);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeOfType<FileSystemCertificateProvider>();
        }

        [Fact]
        public void GetProvider_WithUnsupportedType_ThrowsArgumentException()
        {
            // Arrange
            var invalidProviderType = (CertificateStorageType)999;

            // Act
            var act = () => _factory.GetProvider(invalidProviderType);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage($"Unsupported provider type: {invalidProviderType}");
        }

        [Theory]
        [InlineData(CertificateStorageType.Filefilesystem)]
        [InlineData(CertificateStorageType.Mongodb)]
        public void GetProvider_WithValidType_ReturnsNonNullProvider(CertificateStorageType providerType)
        {
            // Act
            var result = _factory.GetProvider(providerType);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeAssignableTo<ICertificateProvider>();
        }
    }
}