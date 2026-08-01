using Api.Controllers;
using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Moq;

namespace XUnitTest.Mail;

public sealed class NotifierControllerTests
{
    [Fact]
    public async Task Notify_returns_service_response()
    {
        var service = new Mock<INotificationService>();
        var expected = new BaseResponse { IsSuccess = true };
        service.Setup(x => x.NotifyAsync(It.IsAny<NotifyRequest>())).ReturnsAsync(expected);
        var controller = new NotifierController(service.Object);

        var result = await controller.Notify(new NotifyRequest());

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task SendSecretNotification_delegates_to_notify()
    {
        var service = new Mock<INotificationService>();
        var expected = new BaseResponse { IsSuccess = true };
        service.Setup(x => x.NotifyAsync(It.IsAny<NotifyRequest>())).ReturnsAsync(expected);
        var controller = new NotifierController(service.Object);

        var result = await controller.SendSecretNotification(new NotifyRequest());

        result.Should().BeSameAs(expected);
        service.Verify(x => x.NotifyAsync(It.IsAny<NotifyRequest>()), Times.Once);
    }

    [Fact]
    public async Task GetUnreadNotificationsBySubscriptionFilter_returns_service_list()
    {
        var service = new Mock<INotificationService>();
        var list = new List<OfflineNotification> { new(), new() };
        service.Setup(x => x.GetUnreadNotificationsBySubscriptionFilter(
                It.IsAny<GetUnreadNotificationsRequestBySubscriptionFilter>()))
            .ReturnsAsync(list);
        var controller = new NotifierController(service.Object);

        var result = await controller.GetUnreadNotificationsBySubscriptionFilter(
            new GetUnreadNotificationsRequestBySubscriptionFilter());

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task MarkAllNotificationAsRead_returns_service_response()
    {
        var service = new Mock<INotificationService>();
        var expected = new BaseResponse { IsSuccess = true };
        service.Setup(x => x.MarkAllNotificationAsRead()).ReturnsAsync(expected);
        var controller = new NotifierController(service.Object);

        var result = await controller.MarkAllNotificationAsRead();

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task MarkNotificationAsRead_returns_service_response()
    {
        var service = new Mock<INotificationService>();
        var expected = new BaseResponse { IsSuccess = true };
        service.Setup(x => x.MarkNotificationAsRead(It.IsAny<MarkNotificationAsReadRequest>()))
            .ReturnsAsync(expected);
        var controller = new NotifierController(service.Object);

        var result = await controller.MarkNotificationAsRead(
            new MarkNotificationAsReadRequest { Id = "notification-1" });

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task GetNotifications_returns_service_response()
    {
        var service = new Mock<INotificationService>();
        var expected = new GetNotificationsResponse
        {
            Notifications = new List<OfflineNotification>(),
            UnReadNotificationsCount = 3,
            TotalNotificationsCount = 9
        };
        service.Setup(x => x.GetNotificationsAsync(It.IsAny<GetNotificationsRequest>()))
            .ReturnsAsync(expected);
        var controller = new NotifierController(service.Object);

        var result = await controller.GetNotifications(new GetNotificationsRequest());

        result.Should().BeSameAs(expected);
        result.UnReadNotificationsCount.Should().Be(3);
    }
}
