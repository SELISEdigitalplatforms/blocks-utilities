using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using FluentAssertions;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Captcha
{
    public class CaptchaGeneratorTests
    {
        [Fact]
        public void EasyCaptchaGenerator_Generate_ReturnsValidBase64String()
        {
            // Arrange
            var generator = new EasyCaptchaGenerator();
            var captchaString = "ABC123";

            // Act
            var result = generator.Generate(captchaString);

            // Assert
            result.Should().NotBeNullOrEmpty();
            result.Should().MatchRegex("^[A-Za-z0-9+/]*={0,2}$");
            
            var bytes = Convert.FromBase64String(result);
            bytes.Should().NotBeEmpty();
            bytes.Length.Should().BeGreaterThan(0);
        }

        [Fact]
        public void EasyCaptchaGenerator_Generate_WithEmptyString_ReturnsValidBase64()
        {
            // Arrange
            var generator = new EasyCaptchaGenerator();

            // Act
            var result = generator.Generate(string.Empty);

            // Assert
            result.Should().NotBeNullOrEmpty();
            Convert.FromBase64String(result).Should().NotBeEmpty();
        }

        [Fact]
        public void HardCaptchaGenerator_Generate_ReturnsValidBase64String()
        {
            // Arrange
            var generator = new HardCaptchaGenerator();
            var captchaString = "XYZ789";

            // Act
            var result = generator.Generate(captchaString);

            // Assert
            result.Should().NotBeNullOrEmpty();
            result.Should().MatchRegex("^[A-Za-z0-9+/]*={0,2}$");
            
            var bytes = Convert.FromBase64String(result);
            bytes.Should().NotBeEmpty();
            bytes.Length.Should().BeGreaterThan(0);
        }

        [Fact]
        public void HardCaptchaGenerator_Generate_WithMultipleCharacters_AppliesVariedStyling()
        {
            // Arrange
            var generator = new HardCaptchaGenerator();
            var captchaString = "ABCDEF";

            // Act
            var result = generator.Generate(captchaString);

            // Assert
            result.Should().NotBeNullOrEmpty();
            var bytes = Convert.FromBase64String(result);
            bytes.Length.Should().BeGreaterThan(1000);
        }

        [Fact]
        public void HardCaptchaGenerator_Generate_WithEmptyString_ReturnsValidBase64()
        {
            // Arrange
            var generator = new HardCaptchaGenerator();

            // Act
            var result = generator.Generate(string.Empty);

            // Assert
            result.Should().NotBeNullOrEmpty();
            Convert.FromBase64String(result).Should().NotBeEmpty();
        }
    }

    public class CaptchaGeneratorProviderTests
    {
        private readonly Mock<IDbContextProvider> _dbContextProvider = new();
        private readonly Mock<IMongoCollection<CaptchaConfiguration>> _mockCollection = new();
        private readonly Mock<IAsyncCursor<CaptchaConfiguration>> _mockCursor = new();

        [Fact]
        public void GetCaptchaGenerator_WithEasyGenerator_ReturnsEasyCaptchaGenerator()
        {
            // Arrange
            var provider = "test-provider";
            var config = new CaptchaConfiguration
            {
                Provider = provider,
                CaptchaGenerator = "EasyCaptchaGenerator"
            };

            SetupMockCollection(config);
            var captchaProvider = new CaptchaGeneratorProvider(_dbContextProvider.Object);

            // Act
            var generator = captchaProvider.GetCaptchaGenerator(provider);

            // Assert
            generator.Should().NotBeNull();
            generator.Should().BeOfType<EasyCaptchaGenerator>();
        }

        [Fact]
        public void GetCaptchaGenerator_WithHardGenerator_ReturnsHardCaptchaGenerator()
        {
            // Arrange
            var provider = "test-provider";
            var config = new CaptchaConfiguration
            {
                Provider = provider,
                CaptchaGenerator = "HardCaptchaGenerator"
            };

            SetupMockCollection(config);
            var captchaProvider = new CaptchaGeneratorProvider(_dbContextProvider.Object);

            // Act
            var generator = captchaProvider.GetCaptchaGenerator(provider);

            // Assert
            generator.Should().NotBeNull();
            generator.Should().BeOfType<HardCaptchaGenerator>();
        }

        [Fact]
        public void GetCaptchaGenerator_WithNullConfiguration_ReturnsHardCaptchaGenerator()
        {
            // Arrange
            var provider = "non-existent-provider";

            SetupMockCollection(null);
            var captchaProvider = new CaptchaGeneratorProvider(_dbContextProvider.Object);

            // Act
            var generator = captchaProvider.GetCaptchaGenerator(provider);

            // Assert
            generator.Should().NotBeNull();
            generator.Should().BeOfType<HardCaptchaGenerator>();
        }

        [Fact]
        public void GetGeneratorName_WithEasyConfiguration_ReturnsEasyGeneratorName()
        {
            // Arrange
            var provider = "test-provider";
            var config = new CaptchaConfiguration
            {
                Provider = provider,
                CaptchaGenerator = "EasyCaptchaGenerator"
            };

            SetupMockCollection(config);
            var captchaProvider = new CaptchaGeneratorProvider(_dbContextProvider.Object);

            // Act
            var generatorName = captchaProvider.GetGeneratorName(provider);

            // Assert
            generatorName.Should().Be("easycaptchagenerator");
        }

        [Fact]
        public void GetGeneratorName_WithHardConfiguration_ReturnsHardGeneratorName()
        {
            // Arrange
            var provider = "test-provider";
            var config = new CaptchaConfiguration
            {
                Provider = provider,
                CaptchaGenerator = "HardCaptchaGenerator"
            };

            SetupMockCollection(config);
            var captchaProvider = new CaptchaGeneratorProvider(_dbContextProvider.Object);

            // Act
            var generatorName = captchaProvider.GetGeneratorName(provider);

            // Assert
            generatorName.Should().Be("hardcaptchagenerator");
        }

        [Fact]
        public void GetGeneratorName_WithNullConfiguration_ReturnsHardGeneratorName()
        {
            // Arrange
            var provider = "non-existent-provider";

            SetupMockCollection(null);
            var captchaProvider = new CaptchaGeneratorProvider(_dbContextProvider.Object);

            // Act
            var generatorName = captchaProvider.GetGeneratorName(provider);

            // Assert
            generatorName.Should().Be("hardcaptchagenerator");
        }

        [Fact]
        public void GetGeneratorName_WithMixedCaseConfiguration_ReturnsLowerCase()
        {
            // Arrange
            var provider = "test-provider";
            var config = new CaptchaConfiguration
            {
                Provider = provider,
                CaptchaGenerator = "HaRdCaPtChAgEnErAtOr"
            };

            SetupMockCollection(config);
            var captchaProvider = new CaptchaGeneratorProvider(_dbContextProvider.Object);

            // Act
            var generatorName = captchaProvider.GetGeneratorName(provider);

            // Assert
            generatorName.Should().Be("hardcaptchagenerator");
        }

        private void SetupMockCollection(CaptchaConfiguration? config)
        {
            var list = config != null ? new List<CaptchaConfiguration> { config } : new List<CaptchaConfiguration>();

            _mockCursor.Setup(x => x.Current).Returns(list);
            _mockCursor.SetupSequence(x => x.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(config != null)
                .Returns(false);
            _mockCursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(config != null)
                .ReturnsAsync(false);

            _mockCollection.Setup(x => x.FindSync(
                It.IsAny<FilterDefinition<CaptchaConfiguration>>(),
                It.IsAny<FindOptions<CaptchaConfiguration, CaptchaConfiguration>>(),
                It.IsAny<CancellationToken>()))
                .Returns(_mockCursor.Object);

            _dbContextProvider.Setup(x => x.GetCollection<CaptchaConfiguration>(It.IsAny<string>()))
                .Returns(_mockCollection.Object);
        }
    }
}
