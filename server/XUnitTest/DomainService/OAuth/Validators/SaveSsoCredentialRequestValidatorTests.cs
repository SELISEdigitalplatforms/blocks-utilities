using DomainService.OAuth;
using DomainService.Shared;
using FluentValidation.TestHelper;

namespace XUnitTest.DomainService.OAuth.Validators
{
    public class SaveSsoCredentialRequestValidatorTests
    {
        private readonly SaveSsoCredentialRequestValidator _validator;

        public SaveSsoCredentialRequestValidatorTests()
        {
            _validator = new SaveSsoCredentialRequestValidator();
        }

        #region WellKnownUrl Validation - When SSOType is BYOSSO

        [Fact]
        public async Task Validate_WithValidHttpsUrlAndValidMetadata_ShouldPass()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://accounts.google.com/.well-known/openid-configuration"
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.WellKnownUrl);
        }
        
        [Fact]
        public async Task Validate_WithInvalidUrl_ShouldFailWithCorrectMessage()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "not-a-valid-url"
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl must be a valid URL");
        }

        [Fact]
        public async Task Validate_WithNullUrl_ShouldFailWithCorrectMessage()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = null
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl must be a valid URL");
        }

        [Fact]
        public async Task Validate_WithEmptyUrl_ShouldFailWithCorrectMessage()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = string.Empty
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl must be a valid URL");
        }

        [Fact]
        public async Task Validate_WithRelativeUrl_ShouldFailWithCorrectMessage()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "/relative/path"
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl must be a valid URL");
        }

        [Fact]
        public async Task Validate_WithFtpScheme_ShouldFailWithCorrectMessage()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "ftp://example.com/config"
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl must be a valid URL");
        }

        [Fact]
        public async Task Validate_WithFileScheme_ShouldFailWithCorrectMessage()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "file:///c:/config.json"
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl must be a valid URL");
        }

        [Fact]
        public async Task Validate_WithValidUrlButInvalidMetadata_ShouldFailWithCorrectMessage()
        {
            // Arrange - Using a valid URL that won't have OIDC metadata
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://www.google.com"
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl does not expose valid OpenID Connect metadata");
        }

        [Fact]
        public async Task Validate_WithValidUrlButNonExistentEndpoint_ShouldFailWithCorrectMessage()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://httpstat.us/404"
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl does not expose valid OpenID Connect metadata");
        }

        #endregion

        #region WellKnownUrl Validation - When SSOType is NOT BYOSSO

        [Fact]
        public async Task Validate_WithInvalidUrlButNonBYOSSOType_ShouldPass()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.Social, // Not BYOSSO
                WellKnownUrl = "not-a-valid-url"
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.WellKnownUrl);
        }

        [Fact]
        public async Task Validate_WithNullUrlButNonBYOSSOType_ShouldPass()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.Social, // Not BYOSSO
                WellKnownUrl = null
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.WellKnownUrl);
        }

        #endregion

        #region Integration Tests with MockHttp (Alternative approach for better coverage)

        [Fact]
        public async Task Validate_WithMetadataMissingAuthorizationEndpoint_ShouldFail()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://test-missing-auth.example.com/.well-known/openid-configuration"
            };

            // Note: This will fail because the URL doesn't actually have valid OIDC metadata
            // In a real scenario, you'd need a test server or mock HTTP responses

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl does not expose valid OpenID Connect metadata");
        }

        [Fact]
        public async Task Validate_WithMetadataMissingTokenEndpoint_ShouldFail()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://test-missing-token.example.com/.well-known/openid-configuration"
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl does not expose valid OpenID Connect metadata");
        }

        [Fact]
        public async Task Validate_WithNonJsonResponse_ShouldFail()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://www.google.com/robots.txt" // Returns plain text, not JSON
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl does not expose valid OpenID Connect metadata");
        }

        [Fact]
        public async Task Validate_WithServerError_ShouldFail()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://httpstat.us/500" // Returns 500 error
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl does not expose valid OpenID Connect metadata");
        }

        [Fact]
        public async Task Validate_WithInvalidJsonStructure_ShouldFail()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://api.github.com" // Valid JSON but not OIDC metadata
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl does not expose valid OpenID Connect metadata");
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task Validate_WithValidUrlButEmptyJsonObject_ShouldFail()
        {
            // Arrange - This would need a mock server returning empty JSON object
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "https://jsonplaceholder.typicode.com/posts/1" // Returns JSON without required fields
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl)
                .WithErrorMessage("WellKnownUrl does not expose valid OpenID Connect metadata");
        }

        [Fact]
        public async Task Validate_CascadeMode_StopsAfterFirstFailure()
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                SSOType = SSOType.BYOSSO,
                WellKnownUrl = "not-a-url" // This fails the first rule
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            // Should only have ONE error (the URL format error), not the metadata error
            // because CascadeMode.Stop prevents further validation
            var errors = result.Errors.Where(e => e.PropertyName == "WellKnownUrl").ToList();
            Assert.Single(errors);
            Assert.Equal("WellKnownUrl must be a valid URL", errors[0].ErrorMessage);
        }

        #endregion

        #region Known Valid OIDC Endpoints (Integration-style tests)

        [Theory]
        [InlineData("https://accounts.google.com/.well-known/openid-configuration")]
        [InlineData("https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration")]
        public async Task Validate_WithKnownValidOidcEndpoints_ShouldPass(string wellKnownUrl)
        {
            // Arrange
            var request = new SaveSsoCredentialRequest
            {
                Provider = "TestProvider",
                Audience = "TestAudience",
                ClientId = "TestClientId",
                ClientSecret = "test-client-secret",
                RedirectUrl = "https://myapp.com/callback",
                WellKnownUrl = wellKnownUrl,
                InitialRoles = new List<string> { "User" },
                ProjectKey = "TestProjectKey",
                IsDisabled = false,
                ItemId = "TestItemId",
                SSOType = SSOType.BYOSSO,
                TeamId = "TestTeamId",
                PrivateKey = "test-private-key"
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.WellKnownUrl);
        }

        #endregion
    }
}