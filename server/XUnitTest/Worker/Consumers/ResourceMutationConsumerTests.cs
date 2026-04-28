using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Microsoft.Extensions.Logging;
using Moq;
using Worker.Consumers;

namespace XUnitTest.Worker.Consumers
{
    public class ResourceMutationConsumerTests
    {
        private readonly Mock<ILogger<ResourceMutationConsumer>> _logger;
        private readonly Mock<IResourceMutationService> _resourceMutationService;
        private readonly ResourceMutationConsumer _consumer;

        public ResourceMutationConsumerTests()
        {
            _logger = new Mock<ILogger<ResourceMutationConsumer>>();
            _resourceMutationService = new Mock<IResourceMutationService>();
            _consumer = new ResourceMutationConsumer(_logger.Object, _resourceMutationService.Object);
        }

        [Fact]
        public async Task Consume_LogsInformationAndDelegatesToService()
        {
            // Arrange
            var mutationEvent = new ResourceMutationEvent
            {
                ItemId = "resource-123",
                Action = MutationEventType.Create,
                Entity = ResourceEntity.Permission
            };

            _resourceMutationService
                .Setup(x => x.ExecuteResourceMutationCommandAsync(mutationEvent))
                .Returns(Task.CompletedTask);

            // Act
            await _consumer.Consume(mutationEvent);

            // Assert
            _resourceMutationService.Verify(
                x => x.ExecuteResourceMutationCommandAsync(mutationEvent),
                Times.Once);

            _logger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Start Consume for ExecuteResourceMutationCommandAsync")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task Consume_WithDifferentEvent_PassesCorrectEventToService()
        {
            // Arrange
            var mutationEvent = new ResourceMutationEvent
            {
                ItemId = "resource-456",
                Action = MutationEventType.Update,
                Entity = ResourceEntity.Role
            };

            _resourceMutationService
                .Setup(x => x.ExecuteResourceMutationCommandAsync(mutationEvent))
                .Returns(Task.CompletedTask);

            // Act
            await _consumer.Consume(mutationEvent);

            // Assert
            _resourceMutationService.Verify(
                x => x.ExecuteResourceMutationCommandAsync(It.Is<ResourceMutationEvent>(
                    e => e.ItemId == "resource-456" 
                         && e.Action == MutationEventType.Update 
                         && e.Entity == ResourceEntity.Role)),
                Times.Once);
        }

        [Fact]
        public async Task Consume_WhenServiceThrowsException_ExceptionPropagates()
        {
            // Arrange
            var mutationEvent = new ResourceMutationEvent
            {
                ItemId = "resource-789",
                Action = MutationEventType.Delete,
                Entity = ResourceEntity.Group
            };

            var expectedException = new InvalidOperationException("Service error");
            _resourceMutationService
                .Setup(x => x.ExecuteResourceMutationCommandAsync(mutationEvent))
                .ThrowsAsync(expectedException);

            // Act
            Func<Task> act = async () => await _consumer.Consume(mutationEvent);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Service error");

            _resourceMutationService.Verify(
                x => x.ExecuteResourceMutationCommandAsync(mutationEvent),
                Times.Once);
        }

        [Fact]
        public async Task Consume_AlwaysLogsBeforeCallingService()
        {
            // Arrange
            var mutationEvent = new ResourceMutationEvent
            {
                ItemId = "resource-999",
                Action = MutationEventType.Update,
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
                .Setup(x => x.ExecuteResourceMutationCommandAsync(mutationEvent))
                .Callback(() => callOrder.Add("Service"))
                .Returns(Task.CompletedTask);

            // Act
            await _consumer.Consume(mutationEvent);

            // Assert
            callOrder.Should().HaveCount(2);
            callOrder[0].Should().Be("Log");
            callOrder[1].Should().Be("Service");
        }
    }
}
