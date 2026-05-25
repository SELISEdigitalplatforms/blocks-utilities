using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.MagicLink.Service;
using Utility.DomainService.Shared.DTOs;
using Utility.DomainService.Shared.Services;

namespace XUnitTest.MagicLink
{
    public class MagicLinkNotificationServiceTests
    {
        private readonly Mock<ILogger<MagicLinkNotificationService>> _mockLogger;
        private readonly Mock<ICryptoService> _mockCryptoService;
        private readonly Mock<ITenants> _mockTenants;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly Mock<IHttpHelperServices> _mockHttpHelperServices;
        private readonly MagicLinkNotificationService _service;

        public MagicLinkNotificationServiceTests()
        {
            _mockLogger = new Mock<ILogger<MagicLinkNotificationService>>();
            _mockCryptoService = new Mock<ICryptoService>();
            _mockTenants = new Mock<ITenants>();
            _mockConfiguration = new Mock<IConfiguration>();
            _mockHttpHelperServices = new Mock<IHttpHelperServices>();

            SetupDefaultMocks();

            _service = new MagicLinkNotificationService(
                _mockLogger.Object,
                _mockCryptoService.Object,
                _mockTenants.Object,
                _mockConfiguration.Object,
                _mockHttpHelperServices.Object
            );
        }

        private void SetupDefaultMocks()
        {
            _mockConfiguration.Setup(c => c["BlocksAppNotificationReceiver"]).Returns("magic-link");
            _mockConfiguration.Setup(c => c["RootTenantId"]).Returns("root-tenant-id");
            _mockConfiguration.Setup(c => c["NotificationServiceUrl"]).Returns("https://notification.api.com");

            _mockTenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);

            _mockCryptoService.Setup(c => c.Hash(It.IsAny<string>(), It.IsAny<string>())).Returns("hashed-secret");

            _mockHttpHelperServices.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .ReturnsAsync((new NotificationResponse { isSuccess = true }, string.Empty));
        }

        [Fact]
        public async Task NotifyLinkCreatedEvent_ShouldSendNotification_WhenSubscriptionFilterIdProvided()
        {
            // Act
            await _service.NotifyLinkCreatedEvent(true, "link-123", "https://short.com/abc", "sub-filter-123", "project-key");

            // Assert
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task NotifyLinkCreatedEvent_ShouldSkipNotification_WhenSubscriptionFilterIdIsNull()
        {
            // Act
            await _service.NotifyLinkCreatedEvent(true, "link-123", "https://short.com/abc", null, "project-key");

            // Assert
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task NotifyLinkCreatedEvent_ShouldSkipNotification_WhenSubscriptionFilterIdIsEmpty()
        {
            // Act
            await _service.NotifyLinkCreatedEvent(true, "link-123", "https://short.com/abc", "", "project-key");

            // Assert
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task NotifyLinksCreatedEvent_ShouldSendNotification_WhenSubscriptionFilterIdProvided()
        {
            // Act
            await _service.NotifyLinksCreatedEvent(true, 10, 2, "sub-filter-123", "project-key");

            // Assert
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task NotifyLinksCreatedEvent_ShouldSkipNotification_WhenSubscriptionFilterIdIsNull()
        {
            // Act
            await _service.NotifyLinksCreatedEvent(true, 10, 2, null, "project-key");

            // Assert
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task NotifyLinksRemovedEvent_ShouldSendNotification_WhenSubscriptionFilterIdProvided()
        {
            // Act
            await _service.NotifyLinksRemovedEvent(true, 5, "sub-filter-123", "project-key");

            // Assert
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task NotifyLinksRemovedEvent_ShouldSkipNotification_WhenSubscriptionFilterIdIsNull()
        {
            // Act
            await _service.NotifyLinksRemovedEvent(true, 5, null, "project-key");

            // Assert
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task NotifyActionExecutedEvent_ShouldSendNotification_WhenSubscriptionFilterIdProvided()
        {
            // Act
            await _service.NotifyActionExecutedEvent(true, "link-123", 200, null, "sub-filter-123", "project-key");

            // Assert
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task NotifyActionExecutedEvent_ShouldSkipNotification_WhenSubscriptionFilterIdIsNull()
        {
            // Act
            await _service.NotifyActionExecutedEvent(true, "link-123", 200, null, null, "project-key");

            // Assert
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldLogSuccess_WhenNotificationSucceeds()
        {
            // Arrange
            _mockHttpHelperServices.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .ReturnsAsync((new NotificationResponse { isSuccess = true }, string.Empty));

            // Act
            await _service.NotifyLinkCreatedEvent(true, "link-123", "https://short.com/abc", "sub-filter-123", "project-key");

            // Assert - Verify the HTTP call was made
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldLogWarning_WhenNotificationFails()
        {
            // Arrange
            _mockHttpHelperServices.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .ReturnsAsync((new NotificationResponse { isSuccess = false, errors = "Some error" }, string.Empty));

            // Act
            await _service.NotifyLinkCreatedEvent(true, "link-123", "https://short.com/abc", "sub-filter-123", "project-key");

            // Assert - Should complete without throwing
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldHandleNullResponse()
        {
            // Arrange
            _mockHttpHelperServices.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .ReturnsAsync(((NotificationResponse?)null, string.Empty));

            // Act
            await _service.NotifyLinksCreatedEvent(true, 5, 0, "sub-filter-123", "project-key");

            // Assert - Should complete without throwing
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldHandleException()
        {
            // Arrange
            _mockHttpHelperServices.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .ThrowsAsync(new Exception("Network error"));

            // Act
            await _service.NotifyLinksRemovedEvent(true, 3, "sub-filter-123", "project-key");

            // Assert - Should complete without throwing (exception is caught and logged)
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task NotifyActionExecutedEvent_ShouldIncludeErrorMessage_WhenProvided()
        {
            // Arrange
            object? capturedPayload = null;
            _mockHttpHelperServices.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .Callback<object, string, Dictionary<string, string>?, string?, string?>((payload, _, _, _, _) => capturedPayload = payload)
                .ReturnsAsync((new NotificationResponse { isSuccess = true }, string.Empty));

            // Act
            await _service.NotifyActionExecutedEvent(false, "link-123", 500, "Internal server error", "sub-filter-123", "project-key");

            // Assert
            capturedPayload.Should().NotBeNull();
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldUseConfigurationValues()
        {
            // Arrange
            Dictionary<string, string>? capturedHeaders = null;
            string? capturedUrl = null;

            _mockHttpHelperServices.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .Callback<object, string, Dictionary<string, string>?, string?, string?>((_, url, headers, _, _) =>
                {
                    capturedUrl = url;
                    capturedHeaders = headers;
                })
                .ReturnsAsync((new NotificationResponse { isSuccess = true }, string.Empty));

            // Act
            await _service.NotifyLinkCreatedEvent(true, "link-123", "https://short.com/abc", "sub-filter-123", "project-key");

            // Assert
            capturedUrl.Should().Be("https://notification.api.com");
            capturedHeaders.Should().ContainKey("x-blocks-key");
            capturedHeaders.Should().ContainKey("Secret");
            capturedHeaders!["x-blocks-key"].Should().Be("root-tenant-id");
            capturedHeaders["Secret"].Should().Be("hashed-secret");
        }

        [Theory]
        [InlineData(true, "Magic Link Created completed successfully")]
        [InlineData(false, "Magic Link Created failed")]
        public async Task NotifyLinkCreatedEvent_ShouldSetCorrectDescription_BasedOnSuccess(bool success, string expectedDescription)
        {
            // Arrange
            object? capturedPayload = null;
            _mockHttpHelperServices.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .Callback<object, string, Dictionary<string, string>?, string?, string?>((payload, _, _, _, _) => capturedPayload = payload)
                .ReturnsAsync((new NotificationResponse { isSuccess = true }, string.Empty));

            // Act
            await _service.NotifyLinkCreatedEvent(success, "link-123", "https://short.com/abc", "sub-filter-123", "project-key");

            // Assert
            capturedPayload.Should().NotBeNull();
        }

        [Fact]
        public async Task NotifyLinksCreatedEvent_ShouldIncludeSuccessAndFailureCounts()
        {
            // Arrange
            object? capturedPayload = null;
            _mockHttpHelperServices.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .Callback<object, string, Dictionary<string, string>?, string?, string?>((payload, _, _, _, _) => capturedPayload = payload)
                .ReturnsAsync((new NotificationResponse { isSuccess = true }, string.Empty));

            // Act
            await _service.NotifyLinksCreatedEvent(true, 15, 3, "sub-filter-123", "project-key");

            // Assert
            capturedPayload.Should().NotBeNull();
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task NotifyLinksRemovedEvent_ShouldIncludeRemovedCount()
        {
            // Arrange
            object? capturedPayload = null;
            _mockHttpHelperServices.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .Callback<object, string, Dictionary<string, string>?, string?, string?>((payload, _, _, _, _) => capturedPayload = payload)
                .ReturnsAsync((new NotificationResponse { isSuccess = true }, string.Empty));

            // Act
            await _service.NotifyLinksRemovedEvent(false, 7, "sub-filter-123", "project-key");

            // Assert
            capturedPayload.Should().NotBeNull();
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task NotifyActionExecutedEvent_ShouldIncludeStatusCodeAndError()
        {
            // Arrange
            object? capturedPayload = null;
            _mockHttpHelperServices.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .Callback<object, string, Dictionary<string, string>?, string?, string?>((payload, _, _, _, _) => capturedPayload = payload)
                .ReturnsAsync((new NotificationResponse { isSuccess = true }, string.Empty));

            // Act
            await _service.NotifyActionExecutedEvent(false, "link-123", 404, "Not Found", "sub-filter-123", "project-key");

            // Assert
            capturedPayload.Should().NotBeNull();
            _mockHttpHelperServices.Verify(h => h.MakeHttpPostRequest<NotificationResponse>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }
    }
}
