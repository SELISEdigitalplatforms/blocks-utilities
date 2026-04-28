using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Services;
using DomainService.Utilities;
using DomainService.Worker;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Moq;

namespace XUnitTest.DomainService.Worker
{
    public class LogoutAllWorkerServiceTests
    {
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<IAuthenticationRepository> _authenticationRepository = new();
        private readonly Mock<IAuthenticationDomainService> _authenticationDomainService = new();
        private readonly LogoutAllWorkerService _service;

        public LogoutAllWorkerServiceTests()
        {
            _service = new LogoutAllWorkerService(
                _cacheClient.Object,
                _authenticationRepository.Object,
                _authenticationDomainService.Object);
        }

        [Fact]
        public async Task Consume_WithValidEvent_RemovesAllRefreshTokensFromCache()
        {
            // Arrange
            var userId = "user-123";
            var logoutAllEvent = new LogoutAllEvent { UserId = userId };
            var sessions = new List<Session>
            {
                new() { RefreshToken = "token1", UserId = userId, IsActive = true },
                new() { RefreshToken = "token2", UserId = userId, IsActive = true },
                new() { RefreshToken = "token3", UserId = userId, IsActive = true }
            };

            _authenticationRepository
                .Setup(x => x.GetActiveSessionByUserIdAsync(userId))
                .ReturnsAsync(sessions);

            _cacheClient
                .Setup(x => x.RemoveKeyAsync(It.IsAny<string>()))
                .ReturnsAsync(true);

            _authenticationRepository
                .Setup(x => x.UpdateSessionStatusForAllRefreshTokenAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(true);

            _authenticationDomainService
                .Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.Consume(logoutAllEvent);

            // Assert
            _cacheClient.Verify(x => x.RemoveKeyAsync("token1"), Times.Once);
            _cacheClient.Verify(x => x.RemoveKeyAsync("token2"), Times.Once);
            _cacheClient.Verify(x => x.RemoveKeyAsync("token3"), Times.Once);
        }

        [Fact]
        public async Task Consume_WithValidEvent_UpdatesSessionStatusForAllTokens()
        {
            // Arrange
            var userId = "user-456";
            var logoutAllEvent = new LogoutAllEvent { UserId = userId };
            var expectedTokens = new List<string> { "token1", "token2" };
            var sessions = new List<Session>
            {
                new() { RefreshToken = "token1", UserId = userId, IsActive = true },
                new() { RefreshToken = "token2", UserId = userId, IsActive = true }
            };

            _authenticationRepository
                .Setup(x => x.GetActiveSessionByUserIdAsync(userId))
                .ReturnsAsync(sessions);

            _cacheClient
                .Setup(x => x.RemoveKeyAsync(It.IsAny<string>()))
                .ReturnsAsync(true);

            _authenticationRepository
                .Setup(x => x.UpdateSessionStatusForAllRefreshTokenAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(true);

            _authenticationDomainService
                .Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.Consume(logoutAllEvent);

            // Assert
            _authenticationRepository.Verify(
                x => x.UpdateSessionStatusForAllRefreshTokenAsync(
                    It.Is<List<string>>(tokens => 
                        tokens.Count == 2 && 
                        tokens.Contains("token1") && 
                        tokens.Contains("token2"))),
                Times.Once);
        }

        [Fact]
        public async Task Consume_WithValidEvent_CallsProcessTimeline()
        {
            // Arrange
            var userId = "user-789";
            var logoutAllEvent = new LogoutAllEvent { UserId = userId };
            var sessions = new List<Session>
            {
                new() { RefreshToken = "token1", UserId = userId, IsActive = true }
            };

            _authenticationRepository
                .Setup(x => x.GetActiveSessionByUserIdAsync(userId))
                .ReturnsAsync(sessions);

            _cacheClient
                .Setup(x => x.RemoveKeyAsync(It.IsAny<string>()))
                .ReturnsAsync(true);

            _authenticationRepository
                .Setup(x => x.UpdateSessionStatusForAllRefreshTokenAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(true);

            _authenticationDomainService
                .Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.Consume(logoutAllEvent);

            // Assert
            _authenticationDomainService.Verify(
                x => x.SendToQueueAsync(
                    IdpConstants.AuthenticationQueue,
                    It.Is<UserAuthenticationTimelineEvent>(e => 
                        e.UserId == userId &&
                        e.Event == "revoke_access_by_logout_all" &&
                        e.ActionBy == "call_api_to_logout_all" &&
                        e.DeviceInformation.Device == "server")),
                Times.Once);
        }

        [Fact]
        public async Task Consume_WithNoActiveSessions_DoesNotCallCacheOrUpdate()
        {
            // Arrange
            var userId = "user-no-sessions";
            var logoutAllEvent = new LogoutAllEvent { UserId = userId };
            var sessions = new List<Session>();

            _authenticationRepository
                .Setup(x => x.GetActiveSessionByUserIdAsync(userId))
                .ReturnsAsync(sessions);

            _authenticationDomainService
                .Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.Consume(logoutAllEvent);

            // Assert
            _cacheClient.Verify(x => x.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
            _authenticationRepository.Verify(
                x => x.UpdateSessionStatusForAllRefreshTokenAsync(It.IsAny<List<string>>()), 
                Times.Once);
        }

        [Fact]
        public async Task ProcessTimeline_WithValidUserId_SendsCorrectEventToQueue()
        {
            // Arrange
            var userId = "user-timeline";
            _authenticationDomainService
                .Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.ProcessTimeline(userId);

            // Assert
            result.Should().BeTrue();
            _authenticationDomainService.Verify(
                x => x.SendToQueueAsync(
                    IdpConstants.AuthenticationQueue,
                    It.Is<UserAuthenticationTimelineEvent>(e =>
                        e.UserId == userId &&
                        e.Event == "revoke_access_by_logout_all" &&
                        e.ActionBy == "call_api_to_logout_all" &&
                        e.DeviceInformation != null &&
                        e.DeviceInformation.Device == "server")),
                Times.Once);
        }

        [Fact]
        public async Task Consume_WhenCalled_ExecutesInCorrectOrder()
        {
            // Arrange
            var userId = "user-order";
            var logoutAllEvent = new LogoutAllEvent { UserId = userId };
            var sessions = new List<Session>
            {
                new() { RefreshToken = "token1", UserId = userId, IsActive = true }
            };

            var callOrder = new List<string>();

            _authenticationRepository
                .Setup(x => x.GetActiveSessionByUserIdAsync(userId))
                .ReturnsAsync(sessions)
                .Callback(() => callOrder.Add("GetActiveSessions"));

            _cacheClient
                .Setup(x => x.RemoveKeyAsync(It.IsAny<string>()))
                .ReturnsAsync(true)
                .Callback(() => callOrder.Add("RemoveCache"));

            _authenticationRepository
                .Setup(x => x.UpdateSessionStatusForAllRefreshTokenAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(true)
                .Callback(() => callOrder.Add("UpdateStatus"));

            _authenticationDomainService
                .Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask)
                .Callback(() => callOrder.Add("SendToQueue"));

            // Act
            await _service.Consume(logoutAllEvent);

            // Assert
            callOrder.Should().ContainInOrder("GetActiveSessions", "RemoveCache", "UpdateStatus", "SendToQueue");
        }
    }
}
