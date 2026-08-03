using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Messaging;

/// <summary>
/// The hub is the socket entry point, so the interesting behaviour is what it
/// forwards to the notification service and what it does with a malformed
/// command from a client.
/// </summary>
public sealed class NotificationHubTests : IDisposable
{
    private const string ConnectionId = "connection-1";

    private readonly Mock<INotificationService> _service = new();
    private readonly NotificationHub _hub;
    private readonly ActivitySource _source = new("XUnitTest.NotificationHub");
    private readonly ActivityListener _listener;

    public NotificationHubTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "XUnitTest.NotificationHub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(_listener);

        _hub = new NotificationHub(
            _service.Object,
            NullLogger<NotificationHub>.Instance)
        {
            Context = Context()
        };
    }

    public void Dispose()
    {
        _hub.Dispose();
        _listener.Dispose();
        _source.Dispose();
    }

    private static HubCallerContext Context(
        string? tenantId = "tenant-1",
        string? userId = "user-1")
    {
        var claims = new List<Claim>();

        if (tenantId != null)
        {
            claims.Add(new Claim("tenant_id", tenantId));
        }

        if (userId != null)
        {
            claims.Add(new Claim("user_id", userId));
        }

        var context = new Mock<HubCallerContext>();
        context.SetupGet(x => x.ConnectionId).Returns(ConnectionId);
        context.SetupGet(x => x.User).Returns(
            new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));

        return context.Object;
    }

    private static string Command(params string[] userIds) =>
        JsonSerializer.Serialize(new NotifierPayload
        {
            UserIds = [.. userIds],
            SubscriptionFilters =
            [
                new SubscriptionFilter
                {
                    Context = "invoice",
                    ActionName = "approved",
                    Value = "42"
                }
            ]
        });

    [Fact]
    public async Task Connecting_registers_the_connection()
    {
        await _hub.OnConnectedAsync();

        _service.Verify(x => x.CreateConnectionAsync(ConnectionId), Times.Once);
    }

    [Fact]
    public async Task A_failure_while_registering_is_surfaced_not_swallowed()
    {
        _service.Setup(x => x.CreateConnectionAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("store down"));

        var act = () => _hub.OnConnectedAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Disconnecting_removes_the_connection()
    {
        await _hub.OnDisconnectedAsync(null!);

        _service.Verify(x => x.RemoveCollectionAsync(ConnectionId), Times.Once);
    }

    [Fact]
    public async Task A_failure_while_removing_the_connection_is_surfaced()
    {
        _service.Setup(x => x.RemoveCollectionAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("store down"));

        var act = () => _hub.OnDisconnectedAsync(new TimeoutException());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Subscribing_carries_the_connection_id_and_the_filters()
    {
        Subscription? captured = null;
        _service.Setup(x => x.AddSubscriptionAsync(It.IsAny<Subscription>()))
            .Callback<Subscription>(subscription => captured = subscription)
            .ReturnsAsync(new BaseResponse());

        await _hub.Subscribe(Command("user-1", "user-2"));

        captured!.Payload.ConnectionId.Should().Be(ConnectionId);
        captured.Payload.UserIds.Should().Equal("user-1", "user-2");
        captured.Payload.SubscriptionFilters.Should().ContainSingle();
    }

    [Fact]
    public async Task Unsubscribing_carries_the_connection_id_and_the_filters()
    {
        Subscription? captured = null;
        _service.Setup(x => x.RemoveSubscriptionAsync(It.IsAny<Subscription>()))
            .Callback<Subscription>(subscription => captured = subscription)
            .ReturnsAsync(new BaseResponse());

        await _hub.Unsubscribe(Command("user-3"));

        captured!.Payload.ConnectionId.Should().Be(ConnectionId);
        captured.Payload.UserIds.Should().Equal("user-3");
    }

    [Fact]
    public async Task A_malformed_subscribe_command_is_rejected()
    {
        var act = () => _hub.Subscribe("not json");

        await act.Should().ThrowAsync<JsonException>();
        _service.Verify(
            x => x.AddSubscriptionAsync(It.IsAny<Subscription>()),
            Times.Never);
    }

    [Fact]
    public async Task A_malformed_unsubscribe_command_is_rejected()
    {
        var act = () => _hub.Unsubscribe("not json");

        await act.Should().ThrowAsync<JsonException>();
        _service.Verify(
            x => x.RemoveSubscriptionAsync(It.IsAny<Subscription>()),
            Times.Never);
    }

    [Fact]
    public async Task A_null_subscribe_payload_is_rejected_rather_than_subscribing_nobody()
    {
        // "null" deserializes to null, and the hub dereferences it. Pinning the
        // throw so the connection is not silently left without a subscription.
        var act = () => _hub.Subscribe("null");

        await act.Should().ThrowAsync<NullReferenceException>();
        _service.Verify(
            x => x.AddSubscriptionAsync(It.IsAny<Subscription>()),
            Times.Never);
    }

    [Fact]
    public async Task The_ambient_tenant_is_taken_from_the_connection_claims()
    {
        using var activity = _source.StartActivity("connect");
        activity.Should().NotBeNull();
        var captured = CaptureAmbientContextOnConnect();

        await _hub.OnConnectedAsync();

        captured().Should().NotBeNull();
        captured()!.TenantId.Should().Be("tenant-1");
        captured()!.UserId.Should().Be("user-1");
    }

    [Fact]
    public async Task Missing_claims_produce_an_empty_ambient_tenant_rather_than_a_failure()
    {
        using var activity = _source.StartActivity("connect");
        activity.Should().NotBeNull();
        _hub.Context = Context(null, null);
        var captured = CaptureAmbientContextOnConnect();

        await _hub.OnConnectedAsync();

        captured().Should().NotBeNull();
        captured()!.TenantId.Should().BeEmpty();
        captured()!.UserId.Should().BeEmpty();
    }

    [Fact]
    public async Task No_ambient_context_is_established_without_a_current_activity()
    {
        // Outside a traced request the hub deliberately leaves the ambient
        // context alone rather than inventing one.
        BlocksContext.SetContext(null);
        var captured = CaptureAmbientContextOnConnect();

        await _hub.OnConnectedAsync();

        captured().Should().BeNull();
    }

    /// <summary>
    /// The ambient context is written to an AsyncLocal inside the hub method,
    /// which does not flow back out to the caller. Reading it from inside the
    /// mocked service call observes it in the flow that actually has it.
    /// </summary>
    private Func<BlocksContext?> CaptureAmbientContextOnConnect()
    {
        BlocksContext? captured = null;
        _service.Setup(x => x.CreateConnectionAsync(It.IsAny<string>()))
            .Callback(() => captured = BlocksContext.GetContext())
            .Returns(Task.CompletedTask);

        return () => captured;
    }
}
