using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using FluentAssertions;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace XUnitTest.DomainService.OAuth.ResponseModel
{
    public class OAuthResponseTests
    {
        [Fact]
        public void TokenResponse_WithOpenIdScope_IncludesIdToken()
        {
            // Arrange
            var tokenResponse = new TokenResponse
            {
                AccessToken = "access-token-123",
                ExpiresIn = 3600,
                RefreshToken = "refresh-token-456"
            };
            var tokenRequest = new TokenRequest
            {
                Scope = "openid profile"
            };

            // Act
            var result = OAuthResponse.TokenResponse(tokenResponse, tokenRequest);

            // Assert
            var okResult = result as OkObjectResult;
            var jsonResult = JsonSerializer.Serialize(okResult.Value);
            var deserializedResult = JsonSerializer.Deserialize<JsonElement>(jsonResult);
            
            deserializedResult.GetProperty("access_token").GetString().Should().Be("access-token-123");
            deserializedResult.GetProperty("token_type").GetString().Should().Be("Bearer");
            deserializedResult.GetProperty("expires_in").GetInt32().Should().Be(3600);
            deserializedResult.GetProperty("refresh_token").GetString().Should().Be("refresh-token-456");
            deserializedResult.GetProperty("id_token").GetString().Should().Be("access-token-123");
        }

        [Fact]
        public void TokenResponse_WithoutOpenIdScope_ExcludesIdToken()
        {
            // Arrange
            var tokenResponse = new TokenResponse
            {
                AccessToken = "access-token-123",
                ExpiresIn = 3600,
                RefreshToken = "refresh-token-456"
            };
            var tokenRequest = new TokenRequest
            {
                Scope = "profile email"
            };

            // Act
            var result = OAuthResponse.TokenResponse(tokenResponse, tokenRequest);

            // Assert
            var okResult = result as OkObjectResult;
            var jsonResult = JsonSerializer.Serialize(okResult.Value);
            var deserializedResult = JsonSerializer.Deserialize<JsonElement>(jsonResult);
            
            deserializedResult.GetProperty("id_token").ValueKind.Should().Be(JsonValueKind.Null);
        }

        [Fact]
        public void MfaResponse_ReturnsCorrectStructure()
        {
            // Arrange
            var tokenResponse = new TokenResponse
            {
                ErrorDescription = "MFA verification required",
                MfaId = "mfa-12345",
                UserMfa = UserMfaType.TOTP
            };

            // Act
            var result = OAuthResponse.MfaResponse(tokenResponse);

            // Assert
            var okResult = result as OkObjectResult;
            var jsonResult = JsonSerializer.Serialize(okResult.Value);
            var deserializedResult = JsonSerializer.Deserialize<JsonElement>(jsonResult);
            
            deserializedResult.GetProperty("enable_mfa").GetBoolean().Should().BeTrue();
            deserializedResult.GetProperty("message").GetString().Should().Be("MFA verification required");
            deserializedResult.GetProperty("mfaId").GetString().Should().Be("mfa-12345");
            deserializedResult.GetProperty("mfaType").GetInt32().Should().Be((int)UserMfaType.TOTP);
        }

        [Fact]
        public void CaptchaResponse_ReturnsCorrectStructure()
        {
            // Act
            var result = OAuthResponse.CaptchaResponse();

            // Assert
            var okResult = result as OkObjectResult;
            var jsonResult = JsonSerializer.Serialize(okResult.Value);
            var deserializedResult = JsonSerializer.Deserialize<JsonElement>(jsonResult);
            
            deserializedResult.GetProperty("enable_captcha").GetBoolean().Should().BeTrue();
            deserializedResult.GetProperty("message").GetString().Should().Be("Captcha enabled. Please verify.");
        }
    }
}