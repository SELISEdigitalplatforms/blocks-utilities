using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Enums;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Worker.Configuration;
using Worker.Consumers;
using Worker.Consumers.Users;

namespace XUnitTest.Worker.Consumers
{
    public class UserConsumersTests
    {
        #region CreateUserByEmailConsumer Tests

        [Fact]
        public async Task CreateUserByEmailConsumer_Consume_DelegatesToService()
        {
            // Arrange
            var mockService = new Mock<IUserManagementMutationService>();
            var consumer = new CreateUserByEmailConsumer(mockService.Object);
            var userEvent = new CreateUserByEmailEvent
            {
                Email = "test@example.com",
                EventQueue = "test-queue",
                EventType = "UserCreation",
                ProjectKey = "test-project"
            };

            mockService.Setup(x => x.CreateUserByEmailAsync(userEvent))
                .ReturnsAsync(true);

            // Act
            await consumer.Consume(userEvent);

            // Assert
            mockService.Verify(x => x.CreateUserByEmailAsync(userEvent), Times.Once);
        }

        [Fact]
        public async Task CreateUserByEmailConsumer_WhenServiceThrows_PropagatesException()
        {
            // Arrange
            var mockService = new Mock<IUserManagementMutationService>();
            var consumer = new CreateUserByEmailConsumer(mockService.Object);
            var userEvent = new CreateUserByEmailEvent
            {
                Email = "test@example.com",
                EventQueue = "test-queue",
                EventType = "UserCreation",
                ProjectKey = "test-project"
            };

            mockService.Setup(x => x.CreateUserByEmailAsync(userEvent))
                .ThrowsAsync(new InvalidOperationException("Service error"));

            // Act
            Func<Task> act = async () => await consumer.Consume(userEvent);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Service error");
        }

        #endregion

        #region CreateUserConsumer Tests

        [Fact]
        public async Task CreateUserConsumer_Consume_DelegatesToService()
        {
            // Arrange
            var mockService = new Mock<IUserManagementMutationService>();
            var consumer = new CreateUserConsumer(mockService.Object);
            var createRequest = new CreateUserRequest
            {
                Email = "user@example.com",
                UserName = "testuser"
            };

            mockService.Setup(x => x.CreateUserAsync(createRequest))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            // Act
            await consumer.Consume(createRequest);

            // Assert
            mockService.Verify(x => x.CreateUserAsync(createRequest), Times.Once);
        }

        [Fact]
        public async Task CreateUserConsumer_WithDifferentRequest_PassesCorrectRequest()
        {
            // Arrange
            var mockService = new Mock<IUserManagementMutationService>();
            var consumer = new CreateUserConsumer(mockService.Object);
            var createRequest = new CreateUserRequest
            {
                Email = "another@example.com",
                UserName = "anotheruser"
            };

            mockService.Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            // Act
            await consumer.Consume(createRequest);

            // Assert
            mockService.Verify(
                x => x.CreateUserAsync(It.Is<CreateUserRequest>(
                    r => r.Email == "another@example.com" && r.UserName == "anotheruser")),
                Times.Once);
        }

        #endregion

        #region CreateUserViaSsoConsumer Tests

        [Fact]
        public async Task CreateUserViaSsoConsumer_Consume_DelegatesToService()
        {
            // Arrange
            var mockService = new Mock<IUserManagementMutationService>();
            var consumer = new CreateUserViaSsoConsumer(mockService.Object);
            var ssoEvent = new CreateUserViaSsoEvent
            {
                ItemId = "item-123",
                Action = MutationEventType.Create,
                ProjectKey = "test-project"
            };

            mockService.Setup(x => x.ExecuteUserMutationViaSsoCommandAsync(ssoEvent))
                .Returns(Task.CompletedTask);

            // Act
            await consumer.Consume(ssoEvent);

            // Assert
            mockService.Verify(x => x.ExecuteUserMutationViaSsoCommandAsync(ssoEvent), Times.Once);
        }

        [Fact]
        public async Task CreateUserViaSsoConsumer_WithDifferentAction_DelegatesToService()
        {
            // Arrange
            var mockService = new Mock<IUserManagementMutationService>();
            var consumer = new CreateUserViaSsoConsumer(mockService.Object);
            var ssoEvent = new CreateUserViaSsoEvent
            {
                ItemId = "item-456",
                Action = MutationEventType.Update,
                ProjectKey = "another-project"
            };

            mockService.Setup(x => x.ExecuteUserMutationViaSsoCommandAsync(ssoEvent))
                .Returns(Task.CompletedTask);

            // Act
            await consumer.Consume(ssoEvent);

            // Assert
            mockService.Verify(
                x => x.ExecuteUserMutationViaSsoCommandAsync(It.Is<CreateUserViaSsoEvent>(
                    e => e.Action == MutationEventType.Update)),
                Times.Once);
        }

        #endregion

        #region UpdateUserByLoginInfoConsumer Tests

        [Fact]
        public async Task UpdateUserByLoginInfoConsumer_Consume_LogsAndDelegatesToService()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<UpdateUserByLoginInfoConsumer>>();
            var mockService = new Mock<IUserManagementMutationService>();
            var consumer = new UpdateUserByLoginInfoConsumer(mockLogger.Object, mockService.Object);
            var refreshEvent = new RefreshTokenEvent
            {
                UserId = "user-123",
                RefreshToken = "token-abc"
            };

            mockService.Setup(x => x.UpdateUserByLoginInfoAsync(refreshEvent))
                .Returns(Task.CompletedTask);

            // Act
            await consumer.Consume(refreshEvent);

            // Assert
            mockService.Verify(x => x.UpdateUserByLoginInfoAsync(refreshEvent), Times.Once);
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Start Consume for UpdateUserByLoginInfoAsync")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task UpdateUserByLoginInfoConsumer_AlwaysLogsBeforeService()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<UpdateUserByLoginInfoConsumer>>();
            var mockService = new Mock<IUserManagementMutationService>();
            var consumer = new UpdateUserByLoginInfoConsumer(mockLogger.Object, mockService.Object);
            var refreshEvent = new RefreshTokenEvent
            {
                UserId = "user-456",
                RefreshToken = "token-xyz"
            };

            var callOrder = new List<string>();
            mockLogger.Setup(x => x.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(() => callOrder.Add("Log"));

            mockService.Setup(x => x.UpdateUserByLoginInfoAsync(refreshEvent))
                .Callback(() => callOrder.Add("Service"))
                .Returns(Task.CompletedTask);

            // Act
            await consumer.Consume(refreshEvent);

            // Assert
            callOrder.Should().HaveCount(2);
            callOrder[0].Should().Be("Log");
            callOrder[1].Should().Be("Service");
        }

        #endregion

        #region UserStatusChangedConsumer Tests

        [Fact]
        public async Task UserStatusChangedConsumer_Consume_SetsApiKeyAndCallsHttpService()
        {
            // Arrange
            var mockHttpService = new Mock<IHttpService>();
            var mockSettings = new Mock<IOptions<VerioSystemSettings>>();
            mockSettings.Setup(x => x.Value).Returns(new VerioSystemSettings 
            { 
                ApiKey = "f6a2c2e1-7bb5-4967-96bf-c534bb1f6c14",
                BaseUri = "https://variosystems.seliselocal.com/api/business-variosystems/ActivateDeactivateUser"
            });
            var consumer = new UserStatusChangedConsumer(mockHttpService.Object, mockSettings.Object);
            var statusEvent = new UserStatusChangedEvent
            {
                UserId = "user-789",
                IsActive = true
            };

            mockHttpService.Setup(x => x.Put<UserStatusChangedEvent>(It.IsAny<UserStatusChangedEvent>(), It.IsAny<string>(), It.IsAny<string>(), null, default))
                .ReturnsAsync((default(UserStatusChangedEvent), string.Empty));

            // Act
            await consumer.Consume(statusEvent);

            // Assert
            statusEvent.ApiKey.Should().Be("f6a2c2e1-7bb5-4967-96bf-c534bb1f6c14");
            mockHttpService.Verify(
                x => x.Put<UserStatusChangedEvent>(
                    It.Is<UserStatusChangedEvent>(e => e.ApiKey == "f6a2c2e1-7bb5-4967-96bf-c534bb1f6c14"),
                    "https://variosystems.seliselocal.com/api/business-variosystems/ActivateDeactivateUser",
                    It.IsAny<string>(),
                    null,
                    default),
                Times.Once);
        }

        [Fact]
        public async Task UserStatusChangedConsumer_WithDeactivateUser_CallsHttpService()
        {
            // Arrange
            var mockHttpService = new Mock<IHttpService>();
            var mockSettings = new Mock<IOptions<VerioSystemSettings>>();
            mockSettings.Setup(x => x.Value).Returns(new VerioSystemSettings());
            var consumer = new UserStatusChangedConsumer(mockHttpService.Object, mockSettings.Object);
            var statusEvent = new UserStatusChangedEvent
            {
                UserId = "user-999",
                IsActive = false
            };

            mockHttpService.Setup(x => x.Put<UserStatusChangedEvent>(It.IsAny<UserStatusChangedEvent>(), It.IsAny<string>(), It.IsAny<string>(), null, default))
                .ReturnsAsync((default(UserStatusChangedEvent), string.Empty));

            // Act
            await consumer.Consume(statusEvent);

            // Assert
            mockHttpService.Verify(
                x => x.Put<UserStatusChangedEvent>(
                    It.Is<UserStatusChangedEvent>(e => e.IsActive == false && e.UserId == "user-999"),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    null,
                    default),
                Times.Once);
        }

        #endregion

        #region UserMutationConsumer Tests

        [Fact]
        public async Task UserMutationConsumer_Consume_DelegatesToService()
        {
            // Arrange
            var mockService = new Mock<IUserManagementMutationService>();
            var consumer = new UserMutationConsumer(mockService.Object);
            var mutationEvent = new UserMutationEvent
            {
                ItemId = "user-111",
                Action = MutationEventType.Update
            };

            mockService.Setup(x => x.ExecuteUserMutationCommandAsync(mutationEvent))
                .Returns(Task.CompletedTask);

            // Act
            await consumer.Consume(mutationEvent);

            // Assert
            mockService.Verify(x => x.ExecuteUserMutationCommandAsync(mutationEvent), Times.Once);
        }

        [Fact]
        public async Task UserMutationConsumer_WithDifferentAction_PassesCorrectEvent()
        {
            // Arrange
            var mockService = new Mock<IUserManagementMutationService>();
            var consumer = new UserMutationConsumer(mockService.Object);
            var mutationEvent = new UserMutationEvent
            {
                ItemId = "user-222",
                Action = MutationEventType.Delete
            };

            mockService.Setup(x => x.ExecuteUserMutationCommandAsync(mutationEvent))
                .Returns(Task.CompletedTask);

            // Act
            await consumer.Consume(mutationEvent);

            // Assert
            mockService.Verify(
                x => x.ExecuteUserMutationCommandAsync(It.Is<UserMutationEvent>(
                    e => e.ItemId == "user-222" && e.Action == MutationEventType.Delete)),
                Times.Once);
        }

        [Fact]
        public async Task UserMutationConsumer_WhenServiceThrows_PropagatesException()
        {
            // Arrange
            var mockService = new Mock<IUserManagementMutationService>();
            var consumer = new UserMutationConsumer(mockService.Object);
            var mutationEvent = new UserMutationEvent
            {
                ItemId = "user-333",
                Action = MutationEventType.Update
            };

            mockService.Setup(x => x.ExecuteUserMutationCommandAsync(mutationEvent))
                .ThrowsAsync(new InvalidOperationException("Mutation failed"));

            // Act
            Func<Task> act = async () => await consumer.Consume(mutationEvent);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Mutation failed");
        }

        #endregion
    }
}
