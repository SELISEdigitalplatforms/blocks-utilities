using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.OAuth.Services;
using DomainService.Services;
using FluentAssertions;
using Iam.DomainService.Entities;
using Moq;

namespace XUnitTest.DomainService.OAuth.Services
{
    public class ClientUserCodeAuthorizationServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _authenticationRepository = new();
        private readonly Mock<IOAuthJwtAccessTokenManager> _oAuthJwtAccessTokenManager = new();
        private readonly ClientUserCodeAuthorizationService _service;

        public ClientUserCodeAuthorizationServiceTests()
        {
            _service = new ClientUserCodeAuthorizationService(
                _authenticationRepository.Object,
                _oAuthJwtAccessTokenManager.Object);
        }

        [Fact]
        public async Task AuthenticateAsync_WithInvalidClient_ReturnsInvalidClientError()
        {
            // Arrange
            var request = new TokenRequest
            {
                ClientId = "invalid-client-id",
                UserCode = "user-code-123",
                GrantType = "user_code"
            };
            var authConfig = new AuthenticationConfiguration();

            _authenticationRepository
                .Setup(x => x.GetBlocksClientAsync(request.ClientId))
                .ReturnsAsync((BlocksClientConfig?)null);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().Be("invalid_client");
            result.ErrorDescription.Should().Be("Client authentication failed");
            _authenticationRepository.Verify(x => x.GetBlocksClientAsync(request.ClientId), Times.Once);
            _authenticationRepository.Verify(x => x.GetUserCodeAsync(It.IsAny<string>()), Times.Never);
            _oAuthJwtAccessTokenManager.Verify(
                x => x.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<AuthenticationConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()),
                Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WithValidClientAndUserCode_ReturnsSuccessfulTokenResponse()
        {
            // Arrange
            var request = new TokenRequest
            {
                ClientId = "valid-client-id",
                UserCode = "user-code-123",
                GrantType = "user_code"
            };
            var authConfig = new AuthenticationConfiguration();
            var client = new BlocksClientConfig
            {
                ItemId = "valid-client-id",
                ClientName = "Test Client"
            };
            var userCode = new UserCode
            {
                ItemId = "code-456",
                UserId = "user-789",
                Code = "user-code-123",
                ClientId = "client-code-456",
                CodeTtlInMinute = 100,
                Note = "Test note"
            };
            var user = new User
            {
                ItemId = "user-789",
                Email = "test@example.com",
                Active = true,
                IsVarified = true
            };
            var expectedTokenResponse = new TokenResponse
            {
                AccessToken = "access-token-123",
                ExpiresIn = 3600,
                RefreshToken = "refresh-token-456"
            };

            _authenticationRepository
                .Setup(x => x.GetBlocksClientAsync(request.ClientId))
                .ReturnsAsync(client);
            _authenticationRepository
                .Setup(x => x.GetUserCodeAsync(request.UserCode))
                .ReturnsAsync(userCode);
            _authenticationRepository
                .Setup(x => x.GetUserByIdAsync(userCode.UserId))
                .ReturnsAsync(user);
            _oAuthJwtAccessTokenManager
                .Setup(x => x.ManageTokenAsync(request, authConfig, user, It.IsAny<StateInfo?>()))
                .ReturnsAsync(expectedTokenResponse);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.AccessToken.Should().Be("access-token-123");
            result.ExpiresIn.Should().Be(3600);
            result.RefreshToken.Should().Be("refresh-token-456");
            result.Error.Should().BeNullOrEmpty();
            _authenticationRepository.Verify(x => x.GetBlocksClientAsync(request.ClientId), Times.Once);
            _authenticationRepository.Verify(x => x.GetUserCodeAsync(request.UserCode), Times.Once);
            _authenticationRepository.Verify(x => x.GetUserByIdAsync(userCode.UserId), Times.Once);
            _oAuthJwtAccessTokenManager.Verify(x => x.ManageTokenAsync(request, authConfig, user, It.IsAny<StateInfo?>()), Times.Once);
        }
    }
}