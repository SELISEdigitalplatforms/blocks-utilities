using Blocks.Genesis;
using Captcha.DomainService.Configuration;
using FluentAssertions;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Captcha
{
    public class CaptchaConfigurationRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _dbContextProvider;
        private readonly Mock<IMongoCollection<CaptchaConfiguration>> _mockCollection;
        private readonly Mock<IAsyncCursor<CaptchaConfiguration>> _mockCursor;
        private readonly CaptchaConfigurationRepository _repository;

        public CaptchaConfigurationRepositoryTests()
        {
            _dbContextProvider = new Mock<IDbContextProvider>();
            _mockCollection = new Mock<IMongoCollection<CaptchaConfiguration>>();
            _mockCursor = new Mock<IAsyncCursor<CaptchaConfiguration>>();

            _dbContextProvider
                .Setup(x => x.GetCollection<CaptchaConfiguration>("CaptchaConfigurations"))
                .Returns(_mockCollection.Object);

            _repository = new CaptchaConfigurationRepository(_dbContextProvider.Object);
        }

        [Fact]
        public async Task GetByProviderAsync_WithExistingProvider_ReturnsConfiguration()
        {
            // Arrange
            var provider = "recaptcha";
            var expected = new CaptchaConfiguration
            {
                Provider = provider,
                CaptchaKey = "test-key",
                CaptchaSecret = "test-secret",
                IsEnable = true
            };

            _mockCursor.SetupSequence(x => x.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);
            _mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            _mockCursor.Setup(x => x.Current).Returns(new[] { expected });

            _mockCollection
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<CaptchaConfiguration>>(),
                    It.IsAny<FindOptions<CaptchaConfiguration>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockCursor.Object);

            // Act
            var result = await _repository.GetByProviderAsync(provider);

            // Assert
            result.Should().NotBeNull();
            result.Provider.Should().Be(provider);
            result.CaptchaKey.Should().Be("test-key");
            _dbContextProvider.Verify(x => x.GetCollection<CaptchaConfiguration>("CaptchaConfigurations"), Times.Once);
        }

        [Fact]
        public async Task GetByProviderAsync_WithNonExistingProvider_ReturnsNull()
        {
            // Arrange
            _mockCursor.Setup(x => x.MoveNext(It.IsAny<CancellationToken>())).Returns(false);
            _mockCursor.Setup(x => x.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            _mockCursor.Setup(x => x.Current).Returns(Array.Empty<CaptchaConfiguration>());

            _mockCollection
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<CaptchaConfiguration>>(),
                    It.IsAny<FindOptions<CaptchaConfiguration>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockCursor.Object);

            // Act
            var result = await _repository.GetByProviderAsync("non-existing");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task GetCaptchaConfigurationAsync_WithEnabledConfiguration_ReturnsConfiguration()
        {
            // Arrange
            var expected = new CaptchaConfiguration
            {
                Provider = "blocks",
                CaptchaKey = "key",
                CaptchaSecret = "secret",
                IsEnable = true
            };

            _mockCursor.SetupSequence(x => x.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);
            _mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            _mockCursor.Setup(x => x.Current).Returns(new[] { expected });

            _mockCollection
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<CaptchaConfiguration>>(),
                    It.IsAny<FindOptions<CaptchaConfiguration>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockCursor.Object);

            // Act
            var result = await _repository.GetCaptchaConfigurationAsync();

            // Assert
            result.Should().NotBeNull();
            result.IsEnable.Should().BeTrue();
            result.Provider.Should().Be("blocks");
        }

        [Fact]
        public async Task GetCaptchaConfigurationAsync_WithNoEnabledConfiguration_ReturnsNull()
        {
            // Arrange
            _mockCursor.Setup(x => x.MoveNext(It.IsAny<CancellationToken>())).Returns(false);
            _mockCursor.Setup(x => x.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            _mockCursor.Setup(x => x.Current).Returns(Array.Empty<CaptchaConfiguration>());

            _mockCollection
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<CaptchaConfiguration>>(),
                    It.IsAny<FindOptions<CaptchaConfiguration>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockCursor.Object);

            // Act
            var result = await _repository.GetCaptchaConfigurationAsync();

            // Assert
            result.Should().BeNull();
        }
    }
}
