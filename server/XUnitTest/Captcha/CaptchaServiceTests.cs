using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Captcha
{
    public class CaptchaServiceTests
    {
        private readonly Mock<IValidator<CreateCaptchaRequest>> _createCaptchaValidator;
        private readonly Mock<IValidator<SubmitCaptchaRequest>> _submitCaptchaValidator;
        private readonly Mock<ICaptchaProcessor> _captchaProcessor;
        private readonly Mock<ICaptchaConfigurationService> _configurationService;
        private readonly Mock<ILogger<CaptchaService>> _logger;
        private readonly CaptchaService _service;

        public CaptchaServiceTests()
        {
            _createCaptchaValidator = new Mock<IValidator<CreateCaptchaRequest>>();
            _submitCaptchaValidator = new Mock<IValidator<SubmitCaptchaRequest>>();
            _captchaProcessor = new Mock<ICaptchaProcessor>();
            _configurationService = new Mock<ICaptchaConfigurationService>();
            _logger = new Mock<ILogger<CaptchaService>>();

            _service = new CaptchaService(
                _captchaProcessor.Object,
                _createCaptchaValidator.Object,
                _submitCaptchaValidator.Object,
                _logger.Object,
                _configurationService.Object
            );
        }

        #region CreateCaptcha Tests

        [Fact]
        public void CreateCaptcha_WithValidRequest_ReturnsSuccessResponse()
        {
            // Arrange
            var request = new CreateCaptchaRequest { ConfigurationName = "test-config" };
            var validationResult = new ValidationResult();
            var captchaInfo = new CaptchaInformation
            {
                Id = "captcha-123",
                Captcha = "ABC123"
            };

            _createCaptchaValidator.Setup(x => x.Validate(request)).Returns(validationResult);
            _captchaProcessor.Setup(x => x.GetCaptchaInformation(request.ConfigurationName))
                .Returns(captchaInfo);

            // Act
            var result = _service.CreateCaptcha(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.Id.Should().Be("captcha-123");
            result.Captcha.Should().Be("ABC123");
            _createCaptchaValidator.Verify(x => x.Validate(request), Times.Once);
            _captchaProcessor.Verify(x => x.GetCaptchaInformation(request.ConfigurationName), Times.Once);
        }

        [Fact]
        public void CreateCaptcha_WithValidationFailure_ReturnsFailureResponse()
        {
            // Arrange
            var request = new CreateCaptchaRequest { ConfigurationName = "" };
            var validationResult = new ValidationResult(new[]
            {
                new ValidationFailure("ConfigurationName", "Configuration name is required")
            });

            _createCaptchaValidator.Setup(x => x.Validate(request)).Returns(validationResult);

            // Act
            var result = _service.CreateCaptcha(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("ConfigurationName");
            _createCaptchaValidator.Verify(x => x.Validate(request), Times.Once);
            _captchaProcessor.Verify(x => x.GetCaptchaInformation(It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region SubmitCaptchaAsync Tests

        [Fact]
        public async Task SubmitCaptchaAsync_WithValidRequest_ReturnsSuccessResponse()
        {
            // Arrange
            var request = new SubmitCaptchaRequest { Id = "captcha-123", Value = "ABC123" };
            var validationResult = new ValidationResult();
            var verificationCode = "verification-456";

            _submitCaptchaValidator.Setup(x => x.ValidateAsync(request, default))
                .ReturnsAsync(validationResult);
            _captchaProcessor.Setup(x => x.SubmitAndCreateVerificationCodeAsync(request.Id))
                .ReturnsAsync(verificationCode);

            // Act
            var result = await _service.SubmitCaptchaAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.VerificationCode.Should().Be(verificationCode);
            _submitCaptchaValidator.Verify(x => x.ValidateAsync(request, default), Times.Once);
            _captchaProcessor.Verify(x => x.SubmitAndCreateVerificationCodeAsync(request.Id), Times.Once);
        }

        [Fact]
        public async Task SubmitCaptchaAsync_WithValidationFailure_ReturnsFailureResponse()
        {
            // Arrange
            var request = new SubmitCaptchaRequest { Id = null, Value = "ABC123" };
            var validationResult = new ValidationResult(new[]
            {
                new ValidationFailure("Id", "Id cannot be null")
            });

            _submitCaptchaValidator.Setup(x => x.ValidateAsync(request, default))
                .ReturnsAsync(validationResult);

            // Act
            var result = await _service.SubmitCaptchaAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Id");
            _submitCaptchaValidator.Verify(x => x.ValidateAsync(request, default), Times.Once);
            _captchaProcessor.Verify(x => x.SubmitAndCreateVerificationCodeAsync(It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region VerifyCaptchaAsync Tests

        [Fact]
        public async Task VerifyCaptchaAsync_WithValidRequest_ReturnsSuccessResponse()
        {
            // Arrange
            var request = new VerifyCaptchaRequest
            {
                VerificationCode = "verification-456",
                ConfigurationName = "test-config"
            };
            var config = new CaptchaConfiguration { Provider = "bcaptcha" };
            var verificationResult = new VerificationResult
            {
                Verified = true,
                HostName = "test.com"
            };

            _configurationService.Setup(x => x.GetByNameAsync(request.ConfigurationName))
                .ReturnsAsync(config);
            _captchaProcessor.Setup(x => x.VerifyCaptchaAsync(config.Provider, request.VerificationCode))
                .ReturnsAsync(verificationResult);

            // Act
            var result = await _service.VerifyCaptchaAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.Verified.Should().BeTrue();
            result.HostName.Should().Be("test.com");
            _configurationService.Verify(x => x.GetByNameAsync(request.ConfigurationName), Times.Once);
            _captchaProcessor.Verify(x => x.VerifyCaptchaAsync(config.Provider, request.VerificationCode), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task VerifyCaptchaAsync_WithNullOrEmptyVerificationCode_ReturnsFailureResponse(string verificationCode)
        {
            // Arrange
            var request = new VerifyCaptchaRequest
            {
                VerificationCode = verificationCode,
                ConfigurationName = "test-config"
            };

            // Act
            var result = await _service.VerifyCaptchaAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Verified.Should().BeFalse();
            result.Errors.Should().ContainKey("VerificationCode");
            result.Errors["VerificationCode"].Should().Be("Verification code cannot be null or empty.");
            _configurationService.Verify(x => x.GetByNameAsync(It.IsAny<string>()), Times.Never);
            _captchaProcessor.Verify(x => x.VerifyCaptchaAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task VerifyCaptchaAsync_WithNullConfiguration_ReturnsFailureResponse()
        {
            // Arrange
            var request = new VerifyCaptchaRequest
            {
                VerificationCode = "verification-456",
                ConfigurationName = "non-existent-config"
            };

            _configurationService.Setup(x => x.GetByNameAsync(request.ConfigurationName))
                .ReturnsAsync((CaptchaConfiguration)null);

            // Act
            var result = await _service.VerifyCaptchaAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Verified.Should().BeFalse();
            result.Errors.Should().ContainKey("Configuration Provider");
            result.Errors["Configuration Provider"].Should().Be("Configuration Provider is not found.");
            _configurationService.Verify(x => x.GetByNameAsync(request.ConfigurationName), Times.Once);
            _captchaProcessor.Verify(x => x.VerifyCaptchaAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        #endregion
    }
}
