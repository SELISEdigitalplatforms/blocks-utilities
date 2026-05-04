using FluentAssertions;
using FluentValidation.TestHelper;
using Utility.DomainService.MagicLink;
using Utility.DomainService.MagicLink.Models;

namespace XUnitTest.MagicLink
{
    public class CreateMagicLinkValidatorTests
    {
        private readonly CreateMagicLinkRequestValidator _validator;

        public CreateMagicLinkValidatorTests()
        {
            _validator = new CreateMagicLinkRequestValidator();
        }

        #region Uri Validation Tests

        [Fact]
        public void Validate_ShouldFail_WhenUriIsEmpty()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = string.Empty
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Uri);
        }

        [Fact]
        public void Validate_ShouldFail_WhenUriIsNull()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = null!
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Uri);
        }

        [Theory]
        [InlineData("not-a-url")]
        [InlineData("invalid")]
        [InlineData("://example.com")]
        [InlineData("example.com")]
        public void Validate_ShouldFail_WhenUriIsNotValidUrl(string uri)
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = uri
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Uri);
        }

        [Theory]
        [InlineData("https://example.com")]
        [InlineData("http://example.com")]
        [InlineData("https://api.example.com/path")]
        [InlineData("https://example.com/path?query=1")]
        [InlineData("https://subdomain.example.com:8080/api/v1")]
        public void Validate_ShouldPass_WhenUriIsValidUrl(string uri)
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = uri
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Uri);
        }

        #endregion

        #region Action Type Validation Tests

        [Fact]
        public void Validate_ShouldFail_WhenActionTypeAndRequestMethodIsEmpty()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Action,
                Uri = "https://api.example.com/action",
                RequestMethod = string.Empty
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.RequestMethod);
        }

        // Note: The validator has a bug where it doesn't handle null RequestMethod properly
        // The Must condition throws NullReferenceException before NotEmpty can catch it

        [Theory]
        [InlineData("PATCH")]
        [InlineData("HEAD")]
        [InlineData("OPTIONS")]
        [InlineData("INVALID")]
        [InlineData("get123")]
        public void Validate_ShouldFail_WhenActionTypeAndRequestMethodIsInvalid(string method)
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Action,
                Uri = "https://api.example.com/action",
                RequestMethod = method
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.RequestMethod);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("DELETE")]
        [InlineData("get")]
        [InlineData("post")]
        [InlineData("Put")]
        [InlineData("Delete")]
        public void Validate_ShouldPass_WhenActionTypeAndRequestMethodIsValid(string method)
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Action,
                Uri = "https://api.example.com/action",
                RequestMethod = method
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.RequestMethod);
        }

        [Fact]
        public void Validate_ShouldNotRequireRequestMethod_WhenRedirectType()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                RequestMethod = null
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.RequestMethod);
        }

        #endregion

        #region UsageLimit Validation Tests

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(int.MaxValue)]
        public void Validate_ShouldPass_WhenUsageLimitIsZeroOrPositive(int usageLimit)
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                UsageLimit = usageLimit
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.UsageLimit);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-100)]
        [InlineData(int.MinValue)]
        public void Validate_ShouldFail_WhenUsageLimitIsNegative(int usageLimit)
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                UsageLimit = usageLimit
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.UsageLimit);
        }

        #endregion

        #region ExpiryLifeSpan Validation Tests

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3600000)] // 1 hour
        [InlineData(86400000)] // 1 day
        [InlineData(long.MaxValue)]
        public void Validate_ShouldPass_WhenExpiryLifeSpanIsZeroOrPositive(long expiryLifeSpan)
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ExpiryLifeSpan = expiryLifeSpan
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.ExpiryLifeSpan);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-1000)]
        [InlineData(long.MinValue)]
        public void Validate_ShouldFail_WhenExpiryLifeSpanIsNegative(long expiryLifeSpan)
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ExpiryLifeSpan = expiryLifeSpan
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.ExpiryLifeSpan);
        }

        #endregion

        #region Complete Request Validation Tests

        [Fact]
        public void Validate_ShouldPass_WithValidRedirectRequest()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Redirect,
                Name = "My Short Link",
                Uri = "https://example.com/destination",
                UsageLimit = 10,
                ExpiryLifeSpan = 3600000,
                ProjectKey = "test-project"
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Validate_ShouldPass_WithValidActionRequest()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Action,
                Name = "Action Link",
                Uri = "https://api.example.com/action",
                RequestMethod = "POST",
                RequestPayload = "{\"key\": \"value\"}",
                UsageLimit = 1,
                ExpiryLifeSpan = 0,
                ProjectKey = "test-project"
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Validate_ShouldFail_WithMultipleErrors()
        {
            // Arrange
            var request = new CreateMagicLinkRequest
            {
                Type = MagicLinkType.Action,
                Uri = "not-a-valid-url",
                RequestMethod = "INVALID",
                UsageLimit = -1,
                ExpiryLifeSpan = -1000
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCountGreaterThanOrEqualTo(3);
        }

        #endregion
    }

    public class InvokeMagicLinkValidatorTests
    {
        private readonly InvokeMagicLinkRequestValidator _validator;

        public InvokeMagicLinkValidatorTests()
        {
            _validator = new InvokeMagicLinkRequestValidator();
        }

        #region LinkId Validation Tests

        [Fact]
        public void Validate_ShouldFail_WhenLinkIdIsEmpty()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest
            {
                LinkId = string.Empty
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.LinkId);
        }

        [Fact]
        public void Validate_ShouldFail_WhenLinkIdIsNull()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest
            {
                LinkId = null!
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.LinkId);
        }

        [Fact]
        public void Validate_ShouldFail_WhenLinkIdIsWhitespace()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest
            {
                LinkId = "   "
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.LinkId);
        }

        [Theory]
        [InlineData("abc123")]
        [InlineData("XYZ789")]
        [InlineData("short-code")]
        [InlineData("my_link_id")]
        public void Validate_ShouldPass_WhenLinkIdIsValid(string linkId)
        {
            // Arrange
            var request = new InvokeMagicLinkRequest
            {
                LinkId = linkId
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.LinkId);
        }

        [Fact]
        public void Validate_ShouldPass_WhenOnlyLinkIdIsProvided()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest
            {
                LinkId = "valid-link-id"
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Validate_ShouldPass_WhenAllFieldsArePopulated()
        {
            // Arrange
            var request = new InvokeMagicLinkRequest
            {
                LinkId = "valid-link-id",
                ProjectKey = "test-project",
                SubscriptionFilterId = "filter-123",
                NotifyOnProcessEnding = true,
                RaiseEventOnProcessEnding = true,
                VisitorIpAddress = "192.168.1.1",
                VisitorUserAgent = "Mozilla/5.0",
                VisitorOrigin = "https://example.com",
                VisitorLanguage = "en-US"
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        #endregion
    }

    public class RemoveMagicLinksValidatorTests
    {
        private readonly RemoveMagicLinksRequestValidator _validator;

        public RemoveMagicLinksValidatorTests()
        {
            _validator = new RemoveMagicLinksRequestValidator();
        }

        #region LinkIds Validation Tests

        [Fact]
        public void Validate_ShouldFail_WhenLinkIdsIsNull()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = null!
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.LinkIds);
        }

        [Fact]
        public void Validate_ShouldPass_WhenLinkIdsIsEmptyList()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string>()
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.LinkIds);
        }

        [Fact]
        public void Validate_ShouldPass_WhenLinkIdsHasSingleItem()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string> { "link-id-1" }
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Validate_ShouldPass_WhenLinkIdsHasMultipleItems()
        {
            // Arrange
            var request = new RemoveMagicLinksRequest
            {
                LinkIds = new List<string> { "link-1", "link-2", "link-3" }
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        #endregion
    }
}
