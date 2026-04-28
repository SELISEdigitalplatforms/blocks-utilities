using Blocks.Genesis;
using DomainService.OAuth;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.DomainService.OAuth
{
    public class MongodbCertificateProviderTests
    {
        private readonly Mock<ILogger> _logger = new();
        private readonly Mock<IBlocksSecret> _blocksSecret = new();

        public MongodbCertificateProviderTests()
        {
            _blocksSecret.Setup(x => x.DatabaseConnectionString).Returns("mongodb://localhost:27017");
            _blocksSecret.Setup(x => x.RootDatabaseName).Returns("TestDatabase");
        }

        [Fact]
        public void Constructor_WithNullDatabaseConnectionString_ThrowsArgumentNullException()
        {
            // Arrange
            _blocksSecret.Setup(x => x.DatabaseConnectionString).Returns((string)null);
            _blocksSecret.Setup(x => x.RootDatabaseName).Returns("TestDatabase");

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => 
                new MongodbCertificateProvider(_logger.Object, _blocksSecret.Object));
            
            Assert.Contains("connectionString", exception.Message);
        }

        [Fact]
        public void Constructor_WithNullRootDatabaseName_ThrowsArgumentNullException()
        {
            // Arrange
            _blocksSecret.Setup(x => x.DatabaseConnectionString).Returns("mongodb://localhost:27017");
            _blocksSecret.Setup(x => x.RootDatabaseName).Returns((string)null);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new MongodbCertificateProvider(_logger.Object, _blocksSecret.Object));
        }

        [Fact]
        public void Constructor_WithValidDependencies_CreatesInstanceSuccessfully()
        {
            // Arrange & Act
            var provider = new MongodbCertificateProvider(_logger.Object, _blocksSecret.Object);

            // Assert
            Assert.NotNull(provider);
            _blocksSecret.Verify(x => x.DatabaseConnectionString, Times.Once);
            _blocksSecret.Verify(x => x.RootDatabaseName, Times.Once);
        }

        [Fact]
        public async Task GetCertificateAsync_WithNullKey_ReturnsEmptyArray()
        {
            // Arrange
            var provider = new MongodbCertificateProvider(_logger.Object, _blocksSecret.Object);

            // Act
            var result = await provider.GetCertificateAsync(null);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetCertificateAsync_WithEmptyKey_ReturnsEmptyArray()
        {
            // Arrange
            var provider = new MongodbCertificateProvider(_logger.Object, _blocksSecret.Object);

            // Act
            var result = await provider.GetCertificateAsync(string.Empty);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetCertificateAsync_WithNonExistentKey_ReturnsEmptyArray()
        {
            // Arrange
            _blocksSecret.Setup(x => x.DatabaseConnectionString).Returns("mongodb://localhost:27017");
            _blocksSecret.Setup(x => x.RootDatabaseName).Returns("NonExistentDb");

            var provider = new MongodbCertificateProvider(_logger.Object, _blocksSecret.Object);
            var key = "non-existent-key";

            // Act
            var result = await provider.GetCertificateAsync(key);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetCertificateAsync_WithValidKey_ReturnsDecodedCertificate()
        {
            // Arrange
            var provider = new MongodbCertificateProvider(_logger.Object, _blocksSecret.Object);
            var key = "test-certificate-key";

            // Act
            var result = await provider.GetCertificateAsync(key);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<byte[]>(result);
        }

        [Fact]
        public void Constructor_WithInvalidConnectionString_ThrowsMongoConfigurationException()
        {
            // Arrange
            _blocksSecret.Setup(x => x.DatabaseConnectionString).Returns("invalid-connection-string");
            _blocksSecret.Setup(x => x.RootDatabaseName).Returns("TestDatabase");

            // Act & Assert
            Assert.Throws<MongoDB.Driver.MongoConfigurationException>(() => 
                new MongodbCertificateProvider(_logger.Object, _blocksSecret.Object));
        }
    }
}