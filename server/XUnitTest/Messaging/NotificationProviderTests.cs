using System.Linq.Expressions;
using System.Net;
using System.Text;
using DomainService.Configuration;
using DomainService.Entities;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Messaging;

/// <summary>
/// Covers the notifier implementations behind <see cref="INotifier"/> and the
/// two factories that choose between them. Every boundary the providers touch
/// (SignalR clients, the notification store, Firebase over HTTP) is mocked so
/// the assertions are about the calls made and the documents persisted.
/// </summary>
public sealed class NotificationProviderTests
{
    private readonly Mock<INotificationRepository> _repository = new();
    private readonly Mock<IStrategicClientProviderFactory> _clientFactory = new();
    private readonly Mock<IStrategicClientProvider> _clientProvider = new();
    private readonly Mock<IClientProxy> _clientProxy = new();

    public NotificationProviderTests()
    {
        _clientProvider
            .Setup(x => x.GetClientAsync(It.IsAny<NotifierPayload>()))
            .ReturnsAsync(_clientProxy.Object);
        _clientFactory
            .Setup(x => x.GetStrategicClientProvider(
                It.IsAny<NotificationReceiverTypes>()))
            .Returns(_clientProvider.Object);
    }

    private SignalRNotificationServiceProvider SignalRProvider() =>
        new(
            _clientFactory.Object,
            _repository.Object,
            NullLogger<SignalRNotificationServiceProvider>.Instance);

    private static NotificationConfiguration Configuration(
        bool enablePersistence = false,
        NotificationReceiverTypes receiverType =
            NotificationReceiverTypes.UserSpecificReceiverType) => new()
        {
            Name = "invoice-approved",
            NotifyMethod = "ReceiveNotification",
            NotificationType = receiverType,
            ChannelToNotify = NotifierTypes.SignalR,
            EnablePersistence = enablePersistence
        };

    private static NotifyRequest Request(
        List<string>? userIds = null,
        List<SubscriptionFilter>? filters = null,
        string? denormalizedPayload = null,
        bool saveAsObject = false) => new()
        {
            ConnectionId = "connection-1",
            UserIds = userIds ?? ["user-1"],
            SubscriptionFilters = filters,
            ResponseKey = "invoiceId",
            ResponseValue = "42",
            DenormalizedPayload = denormalizedPayload!,
            SaveDenormalizedPayloadAsAnObject = saveAsObject
        };

    private List<OfflineNotification> CapturedSaves()
    {
        var saved = new List<OfflineNotification>();
        _repository
            .Setup(x => x.SaveAsync(It.IsAny<List<OfflineNotification>>()))
            .Callback<List<OfflineNotification>>(saved.AddRange)
            .Returns(Task.CompletedTask);

        return saved;
    }

    [Fact]
    public async Task Notify_pushes_through_the_configured_hub_method()
    {
        await SignalRProvider().Notify(Request(), Configuration());

        _clientProxy.Verify(
            x => x.SendCoreAsync(
                "ReceiveNotification",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Notify_resolves_the_receiver_strategy_from_the_configuration()
    {
        await SignalRProvider().Notify(
            Request(),
            Configuration(
                receiverType: NotificationReceiverTypes.BroadcastReceiverType));

        _clientFactory.Verify(
            x => x.GetStrategicClientProvider(
                NotificationReceiverTypes.BroadcastReceiverType),
            Times.Once);
    }

    [Fact]
    public async Task Nothing_is_persisted_when_persistence_is_switched_off()
    {
        await SignalRProvider().Notify(Request(), Configuration());

        _repository.Verify(
            x => x.SaveAsync(It.IsAny<List<OfflineNotification>>()),
            Times.Never);
    }

    [Fact]
    public async Task One_offline_notification_is_written_per_recipient()
    {
        var saved = CapturedSaves();

        await SignalRProvider().Notify(
            Request(userIds: ["user-1", "user-2"]),
            Configuration(enablePersistence: true));

        saved.Should().HaveCount(2);
        saved.Select(item => item.Payload.UserId)
            .Should().BeEquivalentTo("user-1", "user-2");
        saved.Select(item => item.CorrelationId).Distinct()
            .Should().ContainSingle("one notify is one correlated batch");
        saved.Should().OnlyContain(item =>
            item.Payload.ResponseKey == "invoiceId" &&
            item.Payload.ResponseValue == "42");
    }

    [Fact]
    public async Task Recipients_are_resolved_from_the_subscription_filters_when_given()
    {
        var saved = CapturedSaves();
        _repository
            .Setup(x => x.GetItemsAsync(
                It.IsAny<Expression<Func<NotificationSubscription, bool>>>(),
                It.IsAny<string>()))
            .ReturnsAsync(
            [
                new NotificationSubscription { UserId = "subscriber-1" },
                new NotificationSubscription { UserId = "subscriber-2" },
                new NotificationSubscription { UserId = null }
            ]);

        await SignalRProvider().Notify(
            Request(
                userIds: ["ignored"],
                filters:
                [
                    new SubscriptionFilter
                    {
                        Context = "invoice",
                        ActionName = "approved",
                        Value = "42"
                    }
                ]),
            Configuration(enablePersistence: true));

        // The null user id is dropped rather than persisted as an unreachable row.
        saved.Select(item => item.Payload.UserId)
            .Should().BeEquivalentTo("subscriber-1", "subscriber-2");
    }

    [Fact]
    public async Task The_request_user_ids_survive_when_a_filter_matches_nobody()
    {
        var saved = CapturedSaves();
        _repository
            .Setup(x => x.GetItemsAsync(
                It.IsAny<Expression<Func<NotificationSubscription, bool>>>(),
                It.IsAny<string>()))
            .ReturnsAsync([]);

        await SignalRProvider().Notify(
            Request(
                userIds: ["user-1"],
                filters: [new SubscriptionFilter { Context = "invoice" }]),
            Configuration(enablePersistence: true));

        saved.Select(item => item.Payload.UserId).Should().Equal("user-1");
    }

    [Fact]
    public async Task A_denormalized_payload_is_stored_as_a_document_when_asked_for()
    {
        var saved = CapturedSaves();

        await SignalRProvider().Notify(
            Request(
                denormalizedPayload: """{"invoice":{"total":42}}""",
                saveAsObject: true),
            Configuration(enablePersistence: true));

        object stored = saved.Single().DenormalizedPayload;
        stored.Should().NotBeNull();
        stored.Should().NotBeOfType<string>();
    }

    [Fact]
    public async Task A_denormalized_payload_is_stored_verbatim_by_default()
    {
        var saved = CapturedSaves();

        await SignalRProvider().Notify(
            Request(denormalizedPayload: """{"invoice":{"total":42}}"""),
            Configuration(enablePersistence: true));

        ((object)saved.Single().DenormalizedPayload)
            .Should().Be("""{"invoice":{"total":42}}""");
    }

    [Fact]
    public async Task An_unparsable_document_payload_does_not_stop_the_notification()
    {
        var saved = CapturedSaves();

        await SignalRProvider().Notify(
            Request(denormalizedPayload: "not json", saveAsObject: true),
            Configuration(enablePersistence: true));

        saved.Should().ContainSingle();
    }

    [Fact]
    public async Task Large_audiences_are_persisted_in_batches()
    {
        var batchSizes = new List<int>();
        _repository
            .Setup(x => x.SaveAsync(It.IsAny<List<OfflineNotification>>()))
            .Callback<List<OfflineNotification>>(batch => batchSizes.Add(batch.Count))
            .Returns(Task.CompletedTask);
        var userIds = Enumerable.Range(0, 1501)
            .Select(index => $"user-{index}")
            .ToList();

        await SignalRProvider().Notify(
            Request(userIds: userIds),
            Configuration(enablePersistence: true));

        batchSizes.Should().Equal(1500, 1);
    }

    [Fact]
    public async Task The_user_specific_receiver_targets_the_requested_users_connections()
    {
        var hubContext = HubContext(out var clients);
        _repository
            .Setup(x => x.GetItemsAsync(
                It.IsAny<Expression<Func<NotificationConnection, bool>>>(),
                It.IsAny<string>()))
            .ReturnsAsync(
            [
                new NotificationConnection { ConnectionId = "c1", UserId = "user-1" },
                new NotificationConnection { ConnectionId = "c2", UserId = "user-2" }
            ]);
        var receiver = new UserSpecificReceiver(
            _repository.Object,
            NullLogger<UserSpecificReceiver>.Instance,
            hubContext);

        await receiver.GetClientAsync(
            new NotifierPayload { UserIds = ["user-1", "user-2"] });

        clients.Verify(
            x => x.Clients(
                It.Is<IReadOnlyList<string>>(ids =>
                    ids.Contains("c1") && ids.Contains("c2"))),
            Times.Once);
    }

    [Fact]
    public async Task The_filter_specific_receiver_resolves_connections_then_users()
    {
        var hubContext = HubContext(out var clients);
        _repository
            .Setup(x => x.GetItemsAsync(
                It.IsAny<Expression<Func<NotificationSubscription, bool>>>(),
                It.IsAny<string>()))
            .ReturnsAsync(
            [
                new NotificationSubscription { ConnectionId = "c1" },
                new NotificationSubscription { ConnectionId = "c1" },
                new NotificationSubscription { ConnectionId = "c2" }
            ]);
        _repository
            .Setup(x => x.GetItemsAsync(
                It.IsAny<Expression<Func<NotificationConnection, bool>>>(),
                It.IsAny<string>()))
            .ReturnsAsync(
            [
                new NotificationConnection { ConnectionId = "c1", UserId = "user-1" },
                new NotificationConnection { ConnectionId = "c2", UserId = "user-1" }
            ]);
        var receiver = new FilterSpecificReceiver(
            _repository.Object,
            NullLogger<FilterSpecificReceiver>.Instance,
            hubContext);
        var payload = new NotifierPayload
        {
            SubscriptionFilters = [new SubscriptionFilter { Context = "invoice" }]
        };

        await receiver.GetClientAsync(payload);

        // Duplicate subscriptions must not fan the same connection out twice, and
        // the resolved users are written back so persistence knows who was told.
        clients.Verify(
            x => x.Clients(
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 2)),
            Times.Once);
        payload.UserIds.Should().Equal("user-1");
    }

    [Fact]
    public async Task The_filter_specific_receiver_leaves_user_ids_alone_when_nobody_matches()
    {
        var hubContext = HubContext(out _);
        _repository
            .Setup(x => x.GetItemsAsync(
                It.IsAny<Expression<Func<NotificationSubscription, bool>>>(),
                It.IsAny<string>()))
            .ReturnsAsync([]);
        var receiver = new FilterSpecificReceiver(
            _repository.Object,
            NullLogger<FilterSpecificReceiver>.Instance,
            hubContext);
        var payload = new NotifierPayload
        {
            UserIds = ["preexisting"],
            SubscriptionFilters = [new SubscriptionFilter { Context = "invoice" }]
        };

        await receiver.GetClientAsync(payload);

        payload.UserIds.Should().Equal("preexisting");
    }

    [Fact]
    public async Task The_broadcast_receiver_targets_everyone()
    {
        var hubContext = HubContext(out var clients);
        var proxy = Mock.Of<IClientProxy>();
        clients.SetupGet(x => x.All).Returns(proxy);

        var result = await new BroadcastReceiver(hubContext)
            .GetClientAsync(new NotifierPayload());

        result.Should().BeSameAs(proxy);
    }

    private static IHubContext<NotificationHub> HubContext(
        out Mock<IHubClients> clients)
    {
        var proxy = Mock.Of<IClientProxy>();
        clients = new Mock<IHubClients>();
        clients.Setup(x => x.Clients(It.IsAny<IReadOnlyList<string>>()))
            .Returns(proxy);
        var hubContext = new Mock<IHubContext<NotificationHub>>();
        hubContext.SetupGet(x => x.Clients).Returns(clients.Object);

        return hubContext.Object;
    }

    [Theory]
    [InlineData(NotificationReceiverTypes.BroadcastReceiverType, typeof(BroadcastReceiver))]
    [InlineData(NotificationReceiverTypes.FilterSpecificReceiverType, typeof(FilterSpecificReceiver))]
    [InlineData(NotificationReceiverTypes.UserSpecificReceiverType, typeof(UserSpecificReceiver))]
    public void The_receiver_factory_maps_each_type_to_its_strategy(
        NotificationReceiverTypes type,
        Type expected)
    {
        var provider = ReceiverContainer();

        new StrategicClientProviderFactory(provider)
            .GetStrategicClientProvider(type)
            .Should().BeOfType(expected);
    }

    [Fact]
    public void An_unknown_receiver_type_is_rejected()
    {
        var factory = new StrategicClientProviderFactory(ReceiverContainer());

        var act = () => factory.GetStrategicClientProvider(
            NotificationReceiverTypes.NoReceiverType);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_notifier_factory_maps_each_channel_to_its_provider()
    {
        var provider = NotifierContainer();
        var factory = new NotifierServiceFactory(provider);

        factory.GetNotifierServiceProvider(NotifierTypes.SignalR)
            .Should().BeOfType<SignalRNotificationServiceProvider>();
        factory.GetNotifierServiceProvider(NotifierTypes.Firebase)
            .Should().BeOfType<FirebaseNotificationServiceProvider>();
    }

    [Fact]
    public void An_unknown_notifier_channel_is_rejected()
    {
        var factory = new NotifierServiceFactory(NotifierContainer());

        var act = () => factory.GetNotifierServiceProvider((NotifierTypes)99);

        act.Should().Throw<ArgumentException>();
    }

    private ServiceProvider ReceiverContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_repository.Object);
        services.AddSingleton(HubContext(out _));
        services.AddSingleton<BroadcastReceiver>();
        services.AddSingleton<FilterSpecificReceiver>();
        services.AddSingleton<UserSpecificReceiver>();

        return services.BuildServiceProvider();
    }

    private ServiceProvider NotifierContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_repository.Object);
        services.AddSingleton(_clientFactory.Object);
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().Build());
        services.AddSingleton<SignalRNotificationServiceProvider>();
        services.AddSingleton(serviceProvider =>
            new FirebaseNotificationServiceProvider(
                NullLogger<FirebaseNotificationServiceProvider>.Instance,
                _repository.Object,
                serviceProvider.GetRequiredService<IConfiguration>()));

        return services.BuildServiceProvider();
    }

    private FirebaseNotificationServiceProvider Firebase(
        HttpStatusCode statusCode,
        out List<HttpRequestMessage> requests,
        string responseBody = "{}")
    {
        var captured = new List<HttpRequestMessage>();
        requests = captured;
        var handler = new StubHandler(statusCode, responseBody, captured);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FirebaseUri"] = "https://fcm.example/send"
            })
            .Build();

        return new FirebaseNotificationServiceProvider(
            NullLogger<FirebaseNotificationServiceProvider>.Instance,
            _repository.Object,
            configuration,
            new HttpClient(handler));
    }

    private void FirebaseConfig(FirebaseConfiguration? configuration) =>
        _repository
            .Setup(x => x.GetItemAsync(
                It.IsAny<Expression<Func<FirebaseConfiguration, bool>>>(),
                It.IsAny<string>()))
            .ReturnsAsync(configuration!);

    [Fact]
    public async Task Firebase_posts_one_topic_message_per_recipient()
    {
        FirebaseConfig(new FirebaseConfiguration { AuthorizationKey = "server-key" });
        var provider = Firebase(HttpStatusCode.OK, out var requests);

        await provider.Notify(
            Request(
                userIds: ["user-1", "user-2"],
                denormalizedPayload: """{"title":"hello"}"""),
            Configuration());

        requests.Should().HaveCount(2);
        requests.Should().OnlyContain(request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri!.ToString() == "https://fcm.example/send");
        requests[0].Headers.GetValues("Authorization").Single()
            .Should().Be("key=server-key");
    }

    [Fact]
    public async Task Firebase_sends_nothing_when_no_configuration_is_stored()
    {
        FirebaseConfig(null);
        var provider = Firebase(HttpStatusCode.OK, out var requests);

        await provider.Notify(
            Request(denormalizedPayload: """{"title":"hello"}"""),
            Configuration());

        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_missing_payload_faults_instead_of_being_skipped()
    {
        // Known defect pinned rather than fixed here: the guard clause logs and
        // awaits Task.CompletedTask where it meant to return, so the null
        // payload reaches the JSON deserializer and throws. Anyone fixing the
        // guard has to update this test.
        FirebaseConfig(new FirebaseConfiguration { AuthorizationKey = "server-key" });
        var provider = Firebase(HttpStatusCode.OK, out var requests);

        var act = () => provider.Notify(Request(), Configuration());

        await act.Should().ThrowAsync<ArgumentNullException>();
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Firebase_sends_nothing_when_the_stored_key_is_blank()
    {
        // Same guard-clause defect: a blank key is logged but still used.
        FirebaseConfig(new FirebaseConfiguration { AuthorizationKey = "  " });
        var provider = Firebase(HttpStatusCode.OK, out var requests);

        await provider.Notify(
            Request(denormalizedPayload: """{"title":"hello"}"""),
            Configuration());

        requests.Should().ContainSingle();
        requests[0].Headers.GetValues("Authorization").Single()
            .Should().Be("key=  ");
    }

    [Fact]
    public async Task A_firebase_rejection_is_raised_rather_than_reported_as_delivered()
    {
        FirebaseConfig(new FirebaseConfiguration { AuthorizationKey = "server-key" });
        var provider = Firebase(
            HttpStatusCode.BadRequest,
            out _,
            "InvalidRegistration");

        var act = () => provider.Notify(
            Request(denormalizedPayload: """{"title":"hello"}"""),
            Configuration());

        (await act.Should().ThrowAsync<Exception>())
            .WithMessage("InvalidRegistration");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;
        private readonly List<HttpRequestMessage> _requests;

        public StubHandler(
            HttpStatusCode statusCode,
            string body,
            List<HttpRequestMessage> requests)
        {
            _statusCode = statusCode;
            _body = body;
            _requests = requests;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Add(request);

            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    _body,
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
