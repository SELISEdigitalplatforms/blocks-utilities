using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Services;
using DomainService.Worker;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.DomainService.Worker
{
    public class UserAuthenticationTimelineWorkerServiceTests
    {
        private readonly Mock<ILogger<UserAuthenticationTimelineWorkerService>> _logger = new();
        private readonly Mock<IAuthenticationRepository> _authenticationRepository = new();
        private readonly UserAuthenticationTimelineWorkerService _service;

        public UserAuthenticationTimelineWorkerServiceTests()
        {
            _service = new UserAuthenticationTimelineWorkerService(
                _logger.Object,
                _authenticationRepository.Object);
        }

        [Fact]
        public async Task Consume_WithValidEvent_CallsProcessUserTimelineEvent()
        {
            // Arrange
            var timelineEvent = CreateUserAuthenticationTimelineEvent();

            _authenticationRepository
                .Setup(x => x.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>()))
                .ReturnsAsync(true);

            // Act
            await _service.Consume(timelineEvent);

            // Assert
            _authenticationRepository.Verify(
                x => x.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>()),
                Times.Once);

            _logger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("UserAuthenticationTimelineWorkerService start")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessUserTimelineEvent_WithFullContext_InsertsTimelineWithCorrectData()
        {
            // Arrange
            var timelineEvent = CreateUserAuthenticationTimelineEvent();

            _authenticationRepository
                .Setup(x => x.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.ProcessUserTimelineEvent(timelineEvent);

            // Assert
            result.Should().BeTrue();
            _authenticationRepository.Verify(
                x => x.InsertUserAuthenticationTimelineAsync(
                    It.Is<UserAuthenticationTimeline>(t =>
                        !string.IsNullOrEmpty(t.ItemId) &&
                        t.CreatedBy == timelineEvent.UserId &&
                        t.LastUpdatedBy == timelineEvent.UserId &&
                        t.DeviceInformation == timelineEvent.DeviceInformation &&
                        t.IpAddresses == timelineEvent.IpAddresses &&
                        t.Event == timelineEvent.Event &&
                        t.ActionBy == timelineEvent.ActionBy &&
                        t.CreatedDate != default &&
                        t.LastUpdatedDate != default)),
                Times.Once);
        }

        [Fact]
        public async Task ProcessUserTimelineEvent_WithNullValues_UsesEmptyStringsForNullableFields()
        {
            // Arrange
            var timelineEvent = new UserAuthenticationTimelineEvent
            {
                UserId = "test-user",
                DeviceInformation = null,
                IpAddresses = null,
                Event = null,
                ActionBy = null
            };

            _authenticationRepository
                .Setup(x => x.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.ProcessUserTimelineEvent(timelineEvent);

            // Assert
            result.Should().BeTrue();
            _authenticationRepository.Verify(
                x => x.InsertUserAuthenticationTimelineAsync(
                    It.Is<UserAuthenticationTimeline>(t =>
                        t.IpAddresses == string.Empty &&
                        t.Event == string.Empty &&
                        t.ActionBy == string.Empty &&
                        t.DeviceInformation == null)),
                Times.Once);
        }

        [Fact]
        public async Task ProcessUserTimelineEvent_WithNullContext_HandlesGracefully()
        {
            // Arrange
            _authenticationRepository
                .Setup(x => x.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.ProcessUserTimelineEvent(null);

            // Assert
            result.Should().BeTrue();
            _authenticationRepository.Verify(
                x => x.InsertUserAuthenticationTimelineAsync(
                    It.Is<UserAuthenticationTimeline>(t =>
                        t.CreatedBy == null &&
                        t.LastUpdatedBy == null &&
                        t.DeviceInformation == null &&
                        t.IpAddresses == string.Empty &&
                        t.Event == string.Empty &&
                        t.ActionBy == string.Empty)),
                Times.Once);
        }

        private UserAuthenticationTimelineEvent CreateUserAuthenticationTimelineEvent()
        {
            return new UserAuthenticationTimelineEvent
            {
                UserId = "test-user-id",
                DeviceInformation = new DeviceInformation { Device = "test-device" },
                IpAddresses = "192.168.1.1",
                Event = "test-event",
                ActionBy = "test-action"
            };
        }
    }
}
