using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using FluentAssertions;
using Moq;

namespace XUnitTest.Captcha
{
    public class SubmitCaptchaCommandValidatorTests
    {
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly SubmitCaptchaCommandValidator _validator;

        public SubmitCaptchaCommandValidatorTests()
        {
            _cacheClient = new Mock<ICacheClient>();
            _validator = new SubmitCaptchaCommandValidator(_cacheClient.Object);
        }

        #region Validation Tests

        [Fact]
        public async Task ValidateAsync_WithValidRequest_ReturnsSuccessResult()
        {
            // Arrange
            var request = new SubmitCaptchaRequest
            {
                Id = "captcha-123",
                Value = "ABC123"
            };

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Id))
                .ReturnsAsync("ABC123");

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task ValidateAsync_WithNullOrEmptyId_ReturnsValidationError(string id)
        {
            // Arrange
            var request = new SubmitCaptchaRequest
            {
                Id = id,
                Value = "ABC123"
            };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Id");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task ValidateAsync_WithNullOrEmptyValue_ReturnsValidationError(string value)
        {
            // Arrange
            var request = new SubmitCaptchaRequest
            {
                Id = "captcha-123",
                Value = value
            };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Value" && e.ErrorMessage == "Value can not be null or empty");
        }

        [Fact]
        public async Task ValidateAsync_WithMismatchedValue_ReturnsValidationError()
        {
            // Arrange
            var request = new SubmitCaptchaRequest
            {
                Id = "captcha-123",
                Value = "WRONG123"
            };

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Id))
                .ReturnsAsync("ABC123");

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Value" && e.ErrorMessage == "Value did not match.");
        }

        [Fact]
        public async Task ValidateAsync_WithCaseInsensitiveMatch_ReturnsSuccessResult()
        {
            // Arrange
            var request = new SubmitCaptchaRequest
            {
                Id = "captcha-123",
                Value = "abc123"
            };

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Id))
                .ReturnsAsync("ABC123");

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        #endregion

        #region BeMatchedWithExistingAsync Tests

        [Fact]
        public async Task BeMatchedWithExistingAsync_WithMatchingValue_ReturnsTrue()
        {
            // Arrange
            var request = new SubmitCaptchaRequest
            {
                Id = "captcha-123",
                Value = "ABC123"
            };

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Id))
                .ReturnsAsync("ABC123");

            // Act
            var result = await _validator.BeMatchedWithExistingAsync(request, "ABC123", default);

            // Assert
            result.Should().BeTrue();
            _cacheClient.Verify(x => x.GetStringValueAsync(request.Id), Times.Once);
        }

        [Fact]
        public async Task BeMatchedWithExistingAsync_WithMismatchedValue_ReturnsFalse()
        {
            // Arrange
            var request = new SubmitCaptchaRequest
            {
                Id = "captcha-123",
                Value = "WRONG123"
            };

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Id))
                .ReturnsAsync("ABC123");

            // Act
            var result = await _validator.BeMatchedWithExistingAsync(request, "WRONG123", default);

            // Assert
            result.Should().BeFalse();
            _cacheClient.Verify(x => x.GetStringValueAsync(request.Id), Times.Once);
        }

        [Theory]
        [InlineData("ABC123", "abc123")]
        [InlineData("abc123", "ABC123")]
        [InlineData("AbC123", "aBc123")]
        public async Task BeMatchedWithExistingAsync_WithCaseInsensitiveMatch_ReturnsTrue(string storedValue, string inputValue)
        {
            // Arrange
            var request = new SubmitCaptchaRequest
            {
                Id = "captcha-123",
                Value = inputValue
            };

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Id))
                .ReturnsAsync(storedValue);

            // Act
            var result = await _validator.BeMatchedWithExistingAsync(request, inputValue, default);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task BeMatchedWithExistingAsync_WithNullStoredValue_ReturnsFalse()
        {
            // Arrange
            var request = new SubmitCaptchaRequest
            {
                Id = "captcha-123",
                Value = "ABC123"
            };

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Id))
                .ReturnsAsync((string)null);

            // Act
            var result = await _validator.BeMatchedWithExistingAsync(request, "ABC123", default);

            // Assert
            result.Should().BeFalse();
        }

        #endregion
    }
}
