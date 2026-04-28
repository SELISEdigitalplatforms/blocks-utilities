using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using Microsoft.AspNetCore.Mvc;

namespace XUnitTest.DomainService.OAuth
{
    public class OAuthErrorTests
    {
        [Fact]
        public void InvalidRequest_ReturnsExpectedBadRequest()
        {
            // Act
            var result = OAuthError.InvalidRequest("Custom description", "test-state") as BadRequestObjectResult;

            // Assert
            Assert.NotNull(result);
            Assert.Equal(400, result.StatusCode);
            var value = result.Value;
            Assert.NotNull(value);
            Assert.Equal("invalid_request", value.GetType().GetProperty("error")?.GetValue(value));
            Assert.Equal("Custom description", value.GetType().GetProperty("error_description")?.GetValue(value));
            Assert.Equal("test-state", value.GetType().GetProperty("state")?.GetValue(value));
        }

        [Fact]
        public void UnsupportedGrantType_ReturnsExpectedBadRequest()
        {
            // Act
            var result = OAuthError.UnsupportedGrantType("test-state") as BadRequestObjectResult;

            // Assert
            Assert.NotNull(result);
            Assert.Equal(400, result.StatusCode);
            var value = result.Value;
            Assert.NotNull(value);
            Assert.Equal("unsupported_grant_type", value.GetType().GetProperty("error")?.GetValue(value));
        }

        [Fact]
        public void UnauthorizedClient_ReturnsExpectedBadRequest()
        {
            // Act
            var result = OAuthError.UnauthorizedClient("test-state") as BadRequestObjectResult;

            // Assert
            Assert.NotNull(result);
            Assert.Equal(400, result.StatusCode);
        }

        [Fact]
        public void Error400Response_ReturnsExpectedBadRequest()
        {
            // Act
            var result = OAuthError.Error400Response("test_error", "Test description") as BadRequestObjectResult;

            // Assert
            Assert.NotNull(result);
            Assert.Equal(400, result.StatusCode);
            var value = result.Value;
            Assert.Equal("test_error", value.GetType().GetProperty("error")?.GetValue(value));
            Assert.Equal("Test description", value.GetType().GetProperty("error_description")?.GetValue(value));
        }

        [Fact]
        public void Error401Response_ReturnsExpectedUnauthorized()
        {
            // Act
            var result = OAuthError.Error401Response("test_error", "Test description") as UnauthorizedObjectResult;

            // Assert
            Assert.NotNull(result);
            Assert.Equal(401, result.StatusCode);
        }

        [Theory]
        [InlineData(GrantTypes.Password, "invalid_username_password", "User name or password invalid", 401)]
        [InlineData(GrantTypes.MfaCode, "invalid_request_body", "Code, two_factor_id and mfa_type should not be empty", 400)]
        [InlineData(GrantTypes.AuthCode, "invalid_request_body", "Code, auth code required", 400)]
        [InlineData("unknown_grant_type", "invalid_grant_type", "Unsupported grant type provided", 400)]
        public void InValidResponse_ReturnsExpectedTokenResponse(string grantType, string expectedError, string expectedDescription, int expectedStatusCode)
        {
            // Arrange
            var request = new TokenRequest { GrantType = grantType };

            // Act
            var result = OAuthError.InValidResponse(request);

            // Assert
            Assert.Equal(expectedError, result.Error);
            Assert.Equal(expectedDescription, result.ErrorDescription);
            Assert.Equal(expectedStatusCode, result.StatusCode);
        }

        [Fact]
        public void UserNotActiveOrVerifiedResponse_ReturnsExpectedTokenResponse()
        {
            // Act
            var result = OAuthError.UserNotActiveOrVerifiedResponse();

            // Assert
            Assert.Equal("user_inactive_or_not_verified", result.Error);
            Assert.Equal("User is not active or verified", result.ErrorDescription);
            Assert.Equal(400, result.StatusCode);
        }

        [Fact]
        public void InValidOrganization_ReturnsExpectedTokenResponse()
        {
            // Act
            var result = OAuthError.InValidOrganization("TestOrg");

            // Assert
            Assert.Equal("user_inactive_or_not_verified", result.Error);
            Assert.Equal("User is not exist within TestOrg organization", result.ErrorDescription);
            Assert.Equal(400, result.StatusCode);
        }
    }
}