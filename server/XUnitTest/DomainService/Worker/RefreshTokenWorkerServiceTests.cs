using DomainService.Entities;
using DomainService.Services;
using DomainService.Worker;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.DomainService.Worker
{
    public class RefreshTokenWorkerServiceTests
    {
        private readonly Mock<ILogger<RefreshTokenWorkerService>> _logger = new();
        private readonly Mock<IAuthenticationRepository> _authenticationRepository = new();
        private readonly Mock<IUserRepository> _userRepository = new();
        private readonly RefreshTokenWorkerService _service;

        public RefreshTokenWorkerServiceTests()
        {
            _service = new RefreshTokenWorkerService(
                _logger.Object,
                _authenticationRepository.Object,
                _userRepository.Object);
        }

        [Fact]
        public async Task Consume_WithValidEvent_CallsAllThreeProcessMethods()
        {
            // Arrange
            var refreshTokenEvent = CreateRefreshTokenEvent();
            var user = CreateUser(logInCount: 5);

            SetupRepositoryMocks(user);

            // Act
            await _service.Consume(refreshTokenEvent);

            // Assert
            _authenticationRepository.Verify(x => x.InsertSessionAsync(It.IsAny<Session>()), Times.Once);
            _authenticationRepository.Verify(x => x.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>()), Times.Once);
            _userRepository.Verify(x => x.UpdateUserAsync(It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task UpdateUserByLoginInfoAsync_WithFirstTimeLogin_SetsFirstLoggedInTime()
        {
            // Arrange
            var refreshTokenEvent = CreateRefreshTokenEvent();
            var user = CreateUser(logInCount: 0);

            _userRepository.Setup(x => x.GetUserByIdAsync(refreshTokenEvent.UserId)).ReturnsAsync(user);
            _userRepository.Setup(x => x.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

            // Act
            await _service.UpdateUserByLoginInfoAsync(refreshTokenEvent);

            // Assert
            _userRepository.Verify(x => x.UpdateUserAsync(
                It.Is<User>(u => 
                    u.LogInCount == 1 &&
                    u.FirstLoggedInTime != default &&
                    u.LastLoggedInTime != default &&
                    u.LastLoggedInDeviceInfo.Contains("test-device"))),
                Times.Once);
        }

        [Fact]
        public async Task UpdateUserByLoginInfoAsync_WithExistingLogin_DoesNotUpdateFirstLoggedInTime()
        {
            // Arrange
            var refreshTokenEvent = CreateRefreshTokenEvent();
            var existingFirstLogin = new DateTime(2024, 1, 1);
            var user = CreateUser(logInCount: 5);
            user.FirstLoggedInTime = existingFirstLogin;

            _userRepository.Setup(x => x.GetUserByIdAsync(refreshTokenEvent.UserId)).ReturnsAsync(user);
            _userRepository.Setup(x => x.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

            // Act
            await _service.UpdateUserByLoginInfoAsync(refreshTokenEvent);

            // Assert
            _userRepository.Verify(x => x.UpdateUserAsync(
                It.Is<User>(u => 
                    u.LogInCount == 6 &&
                    u.FirstLoggedInTime == existingFirstLogin &&
                    u.LastLoggedInTime != default)),
                Times.Once);
        }

        [Fact]
        public async Task UpdateUserByLoginInfoAsync_WithNullUser_LogsErrorAndReturns()
        {
            // Arrange
            var refreshTokenEvent = CreateRefreshTokenEvent();

            _userRepository.Setup(x => x.GetUserByIdAsync(refreshTokenEvent.UserId)).ReturnsAsync((User?)null);

            // Act
            await _service.UpdateUserByLoginInfoAsync(refreshTokenEvent);

            // Assert
            _userRepository.Verify(x => x.UpdateUserAsync(It.IsAny<User>()), Times.Never);
            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("User not found")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessSession_WithValidEvent_InsertsSessionWithCorrectData()
        {
            // Arrange
            var refreshTokenEvent = CreateRefreshTokenEvent();

            _authenticationRepository.Setup(x => x.InsertSessionAsync(It.IsAny<Session>())).ReturnsAsync(true);

            // Act
            var result = await _service.ProcessSession(refreshTokenEvent);

            // Assert
            result.Should().BeTrue();
            _authenticationRepository.Verify(
                x => x.InsertSessionAsync(
                    It.Is<Session>(s =>
                        s.RefreshToken == refreshTokenEvent.RefreshToken &&
                        s.TenantId == refreshTokenEvent.TenantId &&
                        s.UserId == refreshTokenEvent.UserId &&
                        s.IpAddresses == refreshTokenEvent.IpAddresses &&
                        s.IsActive == true &&
                        s.DeviceInformation == refreshTokenEvent.DeviceInformation)),
                Times.Once);
        }

        [Fact]
        public async Task ProcessUserTimelineEvent_WithValidEvent_InsertsTimelineWithCorrectData()
        {
            // Arrange
            var refreshTokenEvent = CreateRefreshTokenEvent();

            _authenticationRepository.Setup(x => x.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>())).ReturnsAsync(true);

            // Act
            var result = await _service.ProcessUserTimelineEvent(refreshTokenEvent);

            // Assert
            result.Should().BeTrue();
            _authenticationRepository.Verify(
                x => x.InsertUserAuthenticationTimelineAsync(
                    It.Is<UserAuthenticationTimeline>(t =>
                        !string.IsNullOrEmpty(t.ItemId) &&
                        t.CreatedBy == refreshTokenEvent.UserId &&
                        t.LastUpdatedBy == refreshTokenEvent.UserId &&
                        t.DeviceInformation == refreshTokenEvent.DeviceInformation &&
                        t.IpAddresses == refreshTokenEvent.IpAddresses &&
                        t.Event == "issued_refresh_token" &&
                        t.ActionBy == "RefreshTokenWorkerService")),
                Times.Once);
        }

        [Fact]
        public async Task ProcessUserTimelineEvent_WithNullIpAddresses_UsesEmptyString()
        {
            // Arrange
            var refreshTokenEvent = CreateRefreshTokenEvent();
            refreshTokenEvent.IpAddresses = null;

            _authenticationRepository.Setup(x => x.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>())).ReturnsAsync(true);

            // Act
            var result = await _service.ProcessUserTimelineEvent(refreshTokenEvent);

            // Assert
            result.Should().BeTrue();
            _authenticationRepository.Verify(
                x => x.InsertUserAuthenticationTimelineAsync(
                    It.Is<UserAuthenticationTimeline>(t => t.IpAddresses == string.Empty)),
                Times.Once);
        }

        private RefreshTokenEvent CreateRefreshTokenEvent()
        {
            return new RefreshTokenEvent
            {
                RefreshToken = "test-refresh-token",
                TenantId = "test-tenant",
                IssuedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddDays(30),
                UserId = "test-user-id",
                IpAddresses = "192.168.1.1",
                DeviceInformation = new DeviceInformation { Device = "test-device" }
            };
        }

        private User CreateUser(int logInCount)
        {
            return new User
            {
                ItemId = "test-user-id",
                Email = "test@example.com",
                LogInCount = logInCount,
                FirstLoggedInTime = default,
                LastLoggedInTime = default,
                LastLoggedInDeviceInfo = string.Empty
            };
        }

        private void SetupRepositoryMocks(User user)
        {
            _userRepository.Setup(x => x.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(user);
            _userRepository.Setup(x => x.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _authenticationRepository.Setup(x => x.InsertSessionAsync(It.IsAny<Session>())).ReturnsAsync(true);
            _authenticationRepository.Setup(x => x.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>())).ReturnsAsync(true);
        }
    }
}
