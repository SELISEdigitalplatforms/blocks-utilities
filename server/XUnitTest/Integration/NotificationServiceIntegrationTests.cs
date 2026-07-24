using DomainService.Configuration.Services;
using DomainService.Entities;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Integration;

[Collection(MongoIntegrationCollection.Name)]
public sealed class NotificationServiceIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly NotificationRepository _notifications;
    private readonly ConfigurationRepository _configurations;
    private readonly Mock<INotifierServiceFactory> _factory = new();
    private readonly Mock<INotifier> _notifier = new();
    private readonly NotificationService _service;

    public NotificationServiceIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _notifications = new NotificationRepository(fixture.DbContextProvider);
        _configurations = new ConfigurationRepository(fixture.DbContextProvider);
        _factory.Setup(f => f.GetNotifierServiceProvider(It.IsAny<NotifierTypes>()))
            .Returns(_notifier.Object);
        _service = new NotificationService(
            _notifications,
            new AddSubscriptionRequestValidator(),
            new NotifyRequestValidator(_configurations),
            new Mock<ILogger<NotificationService>>().Object,
            _factory.Object,
            _configurations);
    }

    private static Subscription NewSubscription(string connectionId) => new()
    {
        Payload =
        {
            ConnectionId = connectionId,
            UserIds = ["user-1"],
            SubscriptionFilters =
            [
                new SubscriptionFilter { Context = "orders", ActionName = "created", Value = "42" }
            ]
        }
    };

    [Fact]
    public async Task AddSubscription_persists_and_RemoveSubscription_deletes()
    {
        var connectionId = Guid.NewGuid().ToString();
        var subscription = NewSubscription(connectionId);

        (await _service.AddSubscriptionAsync(subscription)).IsSuccess.Should().BeTrue();
        var stored = await _notifications.GetItemsAsync<NotificationSubscription>(
            s => s.ConnectionId == connectionId);
        stored.Should().ContainSingle();

        (await _service.RemoveSubscriptionAsync(subscription)).IsSuccess.Should().BeTrue();
        (await _notifications.GetItemsAsync<NotificationSubscription>(s => s.ConnectionId == connectionId))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task AddSubscription_with_missing_connection_fails_validation()
    {
        var invalid = new Subscription
        {
            Payload = { ConnectionId = string.Empty, SubscriptionFilters = [] }
        };

        (await _service.AddSubscriptionAsync(invalid)).Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateConnection_then_RemoveCollection_manages_records()
    {
        var connectionId = Guid.NewGuid().ToString();

        await _service.CreateConnectionAsync(connectionId);
        (await _notifications.GetItemsAsync<NotificationConnection>(c => c.ConnectionId == connectionId))
            .Should().ContainSingle();

        await _service.AddSubscriptionAsync(NewSubscription(connectionId));
        await _service.RemoveCollectionAsync(connectionId);

        (await _notifications.GetItemsAsync<NotificationConnection>(c => c.ConnectionId == connectionId))
            .Should().BeEmpty();
        (await _notifications.GetItemsAsync<NotificationSubscription>(s => s.ConnectionId == connectionId))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Notify_dispatches_to_provider_when_configuration_exists()
    {
        var name = "notify-" + Guid.NewGuid().ToString("N");
        await _configurations.SaveAsync(new NotificationConfiguration
        {
            ItemId = Guid.NewGuid().ToString(),
            Name = name,
            ChannelToNotify = NotifierTypes.SignalR,
            NotificationType = NotificationReceiverTypes.BroadcastReceiverType,
            CreatedDate = DateTime.UtcNow
        });

        var response = await _service.NotifyAsync(new NotifyRequest
        {
            ConfigurationName = name,
            ConnectionId = Guid.NewGuid().ToString()
        });

        response.IsSuccess.Should().BeTrue();
        _factory.Verify(f => f.GetNotifierServiceProvider(NotifierTypes.SignalR), Times.Once);
        _notifier.Verify(n => n.Notify(It.IsAny<NotifyRequest>(), It.IsAny<NotificationConfiguration>()), Times.Once);
    }

    [Fact]
    public async Task Notify_fails_when_configuration_missing()
    {
        var response = await _service.NotifyAsync(new NotifyRequest
        {
            ConfigurationName = "does-not-exist-" + Guid.NewGuid().ToString("N")
        });

        response.IsSuccess.Should().BeFalse();
        _notifier.Verify(n => n.Notify(It.IsAny<NotifyRequest>(), It.IsAny<NotificationConfiguration>()), Times.Never);
    }

    [Fact]
    public async Task MarkNotification_and_MarkAll_flag_offline_notifications_as_read()
    {
        var id = Guid.NewGuid().ToString();
        await _fixture.Collection<OfflineNotification>("OfflineNotifications").InsertOneAsync(
            new OfflineNotification
            {
                Id = id,
                CreatedTime = DateTime.UtcNow,
                ReadByUserIds = [],
                Payload = new PayloadData { UserId = string.Empty, SubscriptionFilters = [] }
            });

        (await _service.MarkNotificationAsRead(new MarkNotificationAsReadRequest { Id = id }))
            .IsSuccess.Should().BeTrue();
        var afterOne = await _notifications.GetItemAsync<OfflineNotification>(n => n.Id == id);
        afterOne.ReadByUserIds.Should().Contain(string.Empty);

        (await _service.MarkAllNotificationAsRead()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetNotifications_returns_visible_notifications()
    {
        var id = Guid.NewGuid().ToString();
        await _fixture.Collection<OfflineNotification>("OfflineNotifications").InsertOneAsync(
            new OfflineNotification
            {
                Id = id,
                CreatedTime = DateTime.UtcNow,
                ReadByUserIds = [],
                Payload = new PayloadData { UserId = string.Empty, SubscriptionFilters = [] }
            });

        var response = await _service.GetNotificationsAsync(new GetNotificationsRequest
        {
            Page = 0,
            PageSize = 50,
            IsUnreadOnly = false
        });

        response.Notifications.Should().Contain(n => n.Id == id);
        response.TotalNotificationsCount.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(OfflineNotificationOrder.CreatedTime)]
    [InlineData(OfflineNotificationOrder.ReadStatus)]
    public async Task GetUnreadNotificationsBySubscriptionFilter_returns_matching_notifications(
        OfflineNotificationOrder order)
    {
        var userId = "user-" + Guid.NewGuid().ToString("N");
        var filter = new SubscriptionFilter { Context = "orders", ActionName = "created", Value = "42" };
        await _fixture.Collection<OfflineNotification>("OfflineNotifications").InsertOneAsync(
            new OfflineNotification
            {
                Id = Guid.NewGuid().ToString(),
                CreatedTime = DateTime.UtcNow,
                ReadByUserIds = [],
                Payload = new PayloadData
                {
                    UserId = userId,
                    NotificationType = NotificationReceiverTypes.FilterSpecificReceiverType.ToString(),
                    SubscriptionFilters = [filter]
                }
            });

        var result = await _service.GetUnreadNotificationsBySubscriptionFilter(
            new GetUnreadNotificationsRequestBySubscriptionFilter
            {
                UserId = userId,
                SubscriptionFilterData = filter,
                OrderBy = order
            });

        result.Should().ContainSingle();
    }
}
