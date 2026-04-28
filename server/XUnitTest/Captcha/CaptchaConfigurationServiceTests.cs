using Captcha.DomainService.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Captcha
{
    public class CaptchaConfigurationServiceTests
    {
        private readonly Mock<ICaptchaConfigurationRepository> _repository;
        private readonly CaptchaConfigurationService _service;

        public CaptchaConfigurationServiceTests()
        {
            _repository = new Mock<ICaptchaConfigurationRepository>();
            _service = new CaptchaConfigurationService(_repository.Object);
        }

        [Fact]
        public async Task GetByNameAsync_WithExistingConfiguration_ReturnsConfiguration()
        {
            // Arrange
            var configName = "recaptcha";
            var expected = new CaptchaConfiguration
            {
                Provider = configName,
                CaptchaKey = "test-key",
                CaptchaSecret = "test-secret",
                IsEnable = true
            };

            _repository.Setup(x => x.GetByProviderAsync(configName))
                .ReturnsAsync(expected);

            // Act
            var result = await _service.GetByNameAsync(configName);

            // Assert
            result.Should().NotBeNull();
            result.Provider.Should().Be(configName);
            result.CaptchaKey.Should().Be("test-key");
            _repository.Verify(x => x.GetByProviderAsync(configName), Times.Once);
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

            _repository.Setup(x => x.GetCaptchaConfigurationAsync())
                .ReturnsAsync(expected);

            // Act
            var result = await _service.GetCaptchaConfigurationAsync();

            // Assert
            result.Should().NotBeNull();
            result.IsEnable.Should().BeTrue();
            result.Provider.Should().Be("blocks");
            _repository.Verify(x => x.GetCaptchaConfigurationAsync(), Times.Once);
        }
    }
}
