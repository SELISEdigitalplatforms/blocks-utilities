using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Microsoft.Extensions.Logging;
using Moq;
using Worker.Consumers;

namespace XUnitTest.Worker.Consumers
{
    public class ResourceSetToPermissionMutationConsumerTests
    {
        private readonly Mock<ILogger<ResourceSetToPermissionMutationConsumer>> _logger;
        private readonly Mock<IResourceMutationService> _resourceMutationService;
        private readonly ResourceSetToPermissionMutationConsumer _consumer;

        public ResourceSetToPermissionMutationConsumerTests()
        {
            _logger = new Mock<ILogger<ResourceSetToPermissionMutationConsumer>>();
            _resourceMutationService = new Mock<IResourceMutationService>();
            _consumer = new ResourceSetToPermissionMutationConsumer(_logger.Object, _resourceMutationService.Object);
        }

        [Fact]
        public async Task Consume_LogsInformationAndDelegatesToService()
        {
            // Arrange
            var mutationEvent = new ResourceSetToPermissionMutationEvent
            {
                AddPermissions = new List<string> { "permission-1", "permission-2" },
                RemovePermissions = new List<string> { "permission-3" },
                Slug = "test-role",
                Entity = ResourceEntity.Role
            };

            _resourceMutationService
                .Setup(x => x.ProcessPermissionAsync(mutationEvent))
                .ReturnsAsync(true);

            // Act
            await _consumer.Consume(mutationEvent);

            // Assert
            _resourceMutationService.Verify(
                x => x.ProcessPermissionAsync(mutationEvent),
                Times.Once);

            _logger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Start Consume for ProcessPermissionAsync")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task Consume_WithDifferentEntity_PassesCorrectEventToService()
        {
            // Arrange
            var mutationEvent = new ResourceSetToPermissionMutationEvent
            {
                AddPermissions = new List<string> { "perm-a", "perm-b" },
                RemovePermissions = new List<string>(),
                Slug = "test-group",
                Entity = ResourceEntity.Group
            };

            _resourceMutationService
                .Setup(x => x.ProcessPermissionAsync(mutationEvent))
                .ReturnsAsync(true);

            // Act
            await _consumer.Consume(mutationEvent);

            // Assert
            _resourceMutationService.Verify(
                x => x.ProcessPermissionAsync(It.Is<ResourceSetToPermissionMutationEvent>(
                    e => e.Slug == "test-group" 
                         && e.Entity == ResourceEntity.Group
                         && e.AddPermissions.Count == 2
                         && e.AddPermissions.Contains("perm-a"))),
                Times.Once);
        }

        [Fact]
        public async Task Consume_WhenServiceThrowsException_ExceptionPropagates()
        {
            // Arrange
            var mutationEvent = new ResourceSetToPermissionMutationEvent
            {
                AddPermissions = new List<string> { "permission-x" },
                RemovePermissions = new List<string> { "permission-y" },
                Slug = "failing-role",
                Entity = ResourceEntity.Role
            };

            var expectedException = new InvalidOperationException("Permission processing failed");
            _resourceMutationService
                .Setup(x => x.ProcessPermissionAsync(mutationEvent))
                .ThrowsAsync(expectedException);

            // Act
            Func<Task> act = async () => await _consumer.Consume(mutationEvent);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Permission processing failed");
            
            _resourceMutationService.Verify(
                x => x.ProcessPermissionAsync(mutationEvent),
                Times.Once);
        }

        [Fact]
        public async Task Consume_AlwaysLogsBeforeCallingService()
        {
            // Arrange
            var mutationEvent = new ResourceSetToPermissionMutationEvent
            {
                AddPermissions = new List<string> { "permission-1" },
                RemovePermissions = new List<string>(),
                Slug = "test-permission",
                Entity = ResourceEntity.Permission
            };

            var callOrder = new List<string>();

            _logger.Setup(x => x.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(() => callOrder.Add("Log"));

            _resourceMutationService
                .Setup(x => x.ProcessPermissionAsync(mutationEvent))
                .Callback(() => callOrder.Add("Service"))
                .ReturnsAsync(true);

            // Act
            await _consumer.Consume(mutationEvent);

            // Assert
            callOrder.Should().HaveCount(2);
            callOrder[0].Should().Be("Log");
            callOrder[1].Should().Be("Service");
        }

        [Fact]
        public async Task Consume_WithEmptyPermissionLists_StillCallsService()
        {
            // Arrange
            var mutationEvent = new ResourceSetToPermissionMutationEvent
            {
                AddPermissions = new List<string>(),
                RemovePermissions = new List<string>(),
                Slug = "empty-role",
                Entity = ResourceEntity.Role
            };

            _resourceMutationService
                .Setup(x => x.ProcessPermissionAsync(mutationEvent))
                .ReturnsAsync(true);

            // Act
            await _consumer.Consume(mutationEvent);

            // Assert
            _resourceMutationService.Verify(
                x => x.ProcessPermissionAsync(It.Is<ResourceSetToPermissionMutationEvent>(
                    e => e.AddPermissions.Count == 0 
                         && e.RemovePermissions.Count == 0)),
                Times.Once);
        }
    }
}
