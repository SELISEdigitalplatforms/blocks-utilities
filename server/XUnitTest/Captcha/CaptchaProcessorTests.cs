using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Utilities;
using FluentAssertions;
using Moq;

namespace XUnitTest.Captcha
{
    public class CaptchaProcessorTests
    {
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<ICaptchaGeneratorProvider> _captchaGeneratorProvider = new();
        private readonly Mock<IContextCaptchaIdGeneratorService> _contextCaptchaIdGeneratorService = new();
        private readonly Mock<ICaptchaVerificationServiceProvider> _captchaVerificationServiceProvider = new();
        private readonly Mock<ICaptchaGenerator> _captchaGenerator = new();
        private readonly Mock<ICaptchaVerificationService> _captchaVerificationService = new();
        private readonly CaptchaProcessor _processor;

        public CaptchaProcessorTests()
        {
            _processor = new CaptchaProcessor(
                _cacheClient.Object,
                _captchaGeneratorProvider.Object,
                _contextCaptchaIdGeneratorService.Object,
                _captchaVerificationServiceProvider.Object);
        }

        [Fact]
        public void GetCaptchaInformation_WithValidProvider_ReturnsCompleteInformation()
        {
            // Arrange
            var provider = "test-provider";
            var contextCaptchaId = "test-captcha-id-123";
            var captchaBase64 = "base64encodedimage==";

            _contextCaptchaIdGeneratorService
                .Setup(x => x.GetContextCaptchaId())
                .Returns(contextCaptchaId);

            _captchaGenerator
                .Setup(x => x.Generate(It.IsAny<string>()))
                .Returns(captchaBase64);

            _captchaGeneratorProvider
                .Setup(x => x.GetCaptchaGenerator(provider))
                .Returns(_captchaGenerator.Object);

            _cacheClient
                .Setup(x => x.AddStringValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .Returns(true);

            // Act
            var result = _processor.GetCaptchaInformation(provider);

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be(contextCaptchaId);
            result.Captcha.Should().Be(captchaBase64);

            _contextCaptchaIdGeneratorService.Verify(x => x.GetContextCaptchaId(), Times.Once);
            _captchaGeneratorProvider.Verify(x => x.GetCaptchaGenerator(provider), Times.Once);
            _captchaGenerator.Verify(x => x.Generate(It.Is<string>(s => s.Length == 4)), Times.Once);
            _cacheClient.Verify(x => x.AddStringValue(contextCaptchaId, It.IsAny<string>(), 600), Times.Once);
        }

        [Fact]
        public void GetCaptchaInformation_GeneratesRandomCaptchaValue()
        {
            // Arrange
            var provider = "test-provider";
            var captchaValues = new List<string>();

            _contextCaptchaIdGeneratorService
                .Setup(x => x.GetContextCaptchaId())
                .Returns("test-id");

            _captchaGenerator
                .Setup(x => x.Generate(It.IsAny<string>()))
                .Callback<string>(value => captchaValues.Add(value))
                .Returns("base64");

            _captchaGeneratorProvider
                .Setup(x => x.GetCaptchaGenerator(provider))
                .Returns(_captchaGenerator.Object);

            _cacheClient
                .Setup(x => x.AddStringValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .Returns(true);

            // Act
            _processor.GetCaptchaInformation(provider);
            _processor.GetCaptchaInformation(provider);

            // Assert
            captchaValues.Should().HaveCount(2);
            captchaValues[0].Should().HaveLength(4);
            captchaValues[1].Should().HaveLength(4);
        }

        [Fact]
        public void GetCaptchaInformation_StoresCaptchaValueInCache()
        {
            // Arrange
            var provider = "test-provider";
            var contextCaptchaId = "captcha-id";
            string? storedValue = null;

            _contextCaptchaIdGeneratorService
                .Setup(x => x.GetContextCaptchaId())
                .Returns(contextCaptchaId);

            _captchaGenerator
                .Setup(x => x.Generate(It.IsAny<string>()))
                .Returns("base64");

            _captchaGeneratorProvider
                .Setup(x => x.GetCaptchaGenerator(provider))
                .Returns(_captchaGenerator.Object);

            _cacheClient
                .Setup(x => x.AddStringValue(contextCaptchaId, It.IsAny<string>(), 600))
                .Callback<string, string, long>((key, value, ttl) => storedValue = value)
                .Returns(true);

            // Act
            _processor.GetCaptchaInformation(provider);

            // Assert
            storedValue.Should().NotBeNullOrEmpty();
            storedValue.Should().HaveLength(4);
            _cacheClient.Verify(x => x.AddStringValue(contextCaptchaId, storedValue, 600), Times.Once);
        }

        [Fact]
        public async Task SubmitAndCreateVerificationCodeAsync_ReturnsGuidInNFormat()
        {
            // Arrange
            var captchaId = "test-captcha-id";

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _cacheClient
                .Setup(x => x.RemoveKeyAsync(captchaId))
                .ReturnsAsync(true);

            // Act
            var result = await _processor.SubmitAndCreateVerificationCodeAsync(captchaId);

            // Assert
            result.Should().NotBeNullOrEmpty();
            result.Should().HaveLength(32); // GUID without hyphens
            result.Should().MatchRegex("^[a-f0-9]{32}$");
        }

        [Fact]
        public async Task SubmitAndCreateVerificationCodeAsync_StoresVerificationCodeInCache()
        {
            // Arrange
            var captchaId = "test-captcha-id";

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), "abc.com", 600))
                .ReturnsAsync(true);

            _cacheClient
                .Setup(x => x.RemoveKeyAsync(captchaId))
                .ReturnsAsync(true);

            // Act
            var result = await _processor.SubmitAndCreateVerificationCodeAsync(captchaId);

            // Assert
            _cacheClient.Verify(x => x.AddStringValueAsync(result, "abc.com", 600), Times.Once);
        }

        [Fact]
        public async Task SubmitAndCreateVerificationCodeAsync_RemovesCaptchaIdFromCache()
        {
            // Arrange
            var captchaId = "test-captcha-id";

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            _cacheClient
                .Setup(x => x.RemoveKeyAsync(captchaId))
                .ReturnsAsync(true);

            // Act
            await _processor.SubmitAndCreateVerificationCodeAsync(captchaId);

            // Assert
            _cacheClient.Verify(x => x.RemoveKeyAsync(captchaId), Times.Once);
        }

        [Fact]
        public async Task VerifyCaptchaAsync_WithValidCredentials_ReturnsVerificationResult()
        {
            // Arrange
            var configProvider = "test-provider";
            var verificationCode = "test-verification-code";
            var expectedResult = new VerificationResult
            {
                Verified = true,
                HostName = "test.com"
            };

            _captchaVerificationService
                .Setup(x => x.VerifyAsync(verificationCode))
                .ReturnsAsync(expectedResult);

            _captchaVerificationServiceProvider
                .Setup(x => x.GetCaptchaVerificationService(configProvider))
                .Returns(_captchaVerificationService.Object);

            // Act
            var result = await _processor.VerifyCaptchaAsync(configProvider, verificationCode);

            // Assert
            result.Should().NotBeNull();
            result.Should().Be(expectedResult);
            result.Verified.Should().BeTrue();
            result.HostName.Should().Be("test.com");

            _captchaVerificationServiceProvider.Verify(
                x => x.GetCaptchaVerificationService(configProvider), 
                Times.Once);
            _captchaVerificationService.Verify(
                x => x.VerifyAsync(verificationCode), 
                Times.Once);
        }

        [Fact]
        public async Task VerifyCaptchaAsync_WithInvalidCredentials_ReturnsFailedVerification()
        {
            // Arrange
            var configProvider = "test-provider";
            var verificationCode = "invalid-code";
            var expectedResult = new VerificationResult
            {
                Verified = false,
                HostName = "",
                Errors = new Dictionary<string, string> { { "error", "Invalid verification code" } }
            };

            _captchaVerificationService
                .Setup(x => x.VerifyAsync(verificationCode))
                .ReturnsAsync(expectedResult);

            _captchaVerificationServiceProvider
                .Setup(x => x.GetCaptchaVerificationService(configProvider))
                .Returns(_captchaVerificationService.Object);

            // Act
            var result = await _processor.VerifyCaptchaAsync(configProvider, verificationCode);

            // Assert
            result.Should().NotBeNull();
            result.Verified.Should().BeFalse();
            result.HostName.Should().BeEmpty();
            result.Errors.Should().ContainKey("error");
        }
    }
}
