using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Messaging;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// The seat, plan and cancellation emails sent to a subscription's billing contact (spec 001).
/// </summary>
/// <remarks>
/// Guards against a billing contact being told nothing, or the wrong thing: a mail addressed to
/// nobody, a template rejected by the mail module for a missing key, a cancellation date a day
/// early, or the canceller's own words placed raw into a subject line.
/// </remarks>
public sealed class SubscriptionNotificationEmailServiceTests
{
    private static readonly string[] AllKeys =
    [
        "DisplayName", "PlanName", "PlanCode", "PreviousPlanName", "ActorName",
        "QuantityChanges", "EffectiveDate", "CancellationReason"
    ];

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();
    private readonly Mock<IMessageClient> _messages = new();
    private readonly Mock<IMailDeliveryReporter> _reports = new();
    private readonly Mock<ISubscriptionAssignmentRepository> _assignments = new();
    private readonly List<MailDeliveryReportRequest> _recorded = [];
    private ConsumerMessage<SendMail>? _queued;

    public SubscriptionNotificationEmailServiceTests()
    {
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                "tenant-1", "subscription-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Subscription());
        _accounts
            .Setup(repository => repository.GetAsync(
                "tenant-1", "account-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount
            {
                BillingEmail = " Billing@Example.com ",
                BillingName = "Ada Lovelace"
            });
        _messages
            .Setup(client => client.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
            .Callback<ConsumerMessage<SendMail>>(message => _queued = message)
            .Returns(Task.CompletedTask);
        _reports
            .Setup(reporter => reporter.RecordAsync(
                It.IsAny<MailDeliveryReportRequest>(), It.IsAny<CancellationToken>()))
            .Callback<MailDeliveryReportRequest, CancellationToken>((report, _) => _recorded.Add(report))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task An_applied_seat_change_mails_the_billing_contact_each_item_from_old_to_new()
    {
        var lifecycleEvent = Event(SubscriptionConstants.SubscriptionQuantityChanged);
        lifecycleEvent.ActorName = "Grace Hopper";
        lifecycleEvent.QuantityChanges =
        [
            new LifecycleQuantityChange { ItemKey = "user", UnitLabel = "Seats", PreviousQuantity = 5, Quantity = 10 },
            new LifecycleQuantityChange { ItemKey = "project", UnitLabel = "", PreviousQuantity = 2, Quantity = 3 }
        ];

        await Service().SendAsync(lifecycleEvent, CancellationToken.None);

        _queued.Should().NotBeNull("an applied change is news the billing contact must receive");
        _queued!.ConsumerName.Should().Be(SubscriptionConstants.MailQueue);
        _queued.Payload.Purpose.Should().Be(SubscriptionConstants.QuantityChangedMailPurpose);
        _queued.Payload.To.Should().Equal(["billing@example.com"],
            "the mail module matches recipients by exact address");
        _queued.Payload.Language.Should().Be(SubscriptionConstants.DefaultMailLanguage);
        _queued.Payload.CorrelationId.Should().Be("event-1");
        _queued.Payload.BodyDataContext.Should().Contain(new Dictionary<string, string>
        {
            ["DisplayName"] = "Ada Lovelace",
            ["PlanName"] = "Team",
            ["PlanCode"] = "team",
            ["ActorName"] = "Grace Hopper",
            ["QuantityChanges"] = "Seats: 5 → 10; project: 2 → 3",
            ["EffectiveDate"] = "2026-10-09"
        }, "an item with no label still has to be named, so its key stands in");

        _recorded.Should().ContainSingle().Which.Should().Match<MailDeliveryReportRequest>(report =>
            report.Source == MailDeliveryReportSource.SubscriptionNotification &&
            report.Outcome == MailDeliveryReportOutcome.Published &&
            report.SubjectId == "subscription-1");
    }

    [Theory]
    [InlineData(SubscriptionConstants.SubscriptionQuantityChanged)]
    [InlineData(SubscriptionConstants.SubscriptionPlanChanged)]
    [InlineData(SubscriptionConstants.SubscriptionCancellationRequested)]
    [InlineData(SubscriptionConstants.SubscriptionCanceled)]
    [InlineData(SubscriptionConstants.SubscriptionCancellationWithdrawn)]
    public async Task Every_purpose_sends_every_key_even_when_its_value_is_absent(string eventType)
    {
        await Service().SendAsync(Event(eventType), CancellationToken.None);

        _queued!.Payload.Purpose.Should().Be(SubscriptionConstants.NotificationMailPurposes[eventType]);
        _queued.Payload.BodyDataContext.Keys.Should().BeEquivalentTo(AllKeys,
            "the mail module silently drops a mail whose template names a key the context lacks");
        _queued.Payload.BodyDataContext.Values.Should().NotContainNulls();
    }

    [Fact]
    public async Task A_plan_change_names_the_plan_left_and_the_plan_arrived_at()
    {
        var lifecycleEvent = Event(SubscriptionConstants.SubscriptionPlanChanged);
        lifecycleEvent.PreviousPlanName = "Starter";

        await Service().SendAsync(lifecycleEvent, CancellationToken.None);

        _queued!.Payload.BodyDataContext["PreviousPlanName"].Should().Be("Starter");
        _queued.Payload.BodyDataContext["PlanName"].Should().Be("Team");
    }

    [Fact]
    public async Task A_requested_cancellation_states_the_local_day_access_ends()
    {
        var lifecycleEvent = Event(SubscriptionConstants.SubscriptionCancellationRequested);
        // Midnight starting 1 November in Zurich, as billing stores it.
        lifecycleEvent.CurrentPeriodEndUtc = new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc);

        await Service().SendAsync(lifecycleEvent, CancellationToken.None);

        _queued!.Payload.BodyDataContext["EffectiveDate"].Should().Be("2026-11-01",
            "a UTC date would tell a Zurich customer they lose access a day before they do");
    }

    [Fact]
    public async Task An_unknown_timezone_falls_back_to_the_utc_date_rather_than_failing_the_mail()
    {
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                "tenant-1", "subscription-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Subscription("Not/AZone"));
        var lifecycleEvent = Event(SubscriptionConstants.SubscriptionCanceled);
        lifecycleEvent.CurrentPeriodEndUtc = new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc);

        await Service().SendAsync(lifecycleEvent, CancellationToken.None);

        _queued!.Payload.BodyDataContext["EffectiveDate"].Should().Be("2026-10-31");
    }

    [Fact]
    public async Task The_cancellation_reason_reaches_the_body_but_never_the_subject()
    {
        var lifecycleEvent = Event(SubscriptionConstants.SubscriptionCancellationRequested);
        lifecycleEvent.CancellationReason = "<b>too expensive</b>";

        await Service().SendAsync(lifecycleEvent, CancellationToken.None);

        _queued!.Payload.BodyDataContext["CancellationReason"].Should().Be("<b>too expensive</b>",
            "the mail module encodes body values itself; encoding here would show the customer &lt;b&gt;");
        _queued.Payload.SubjectDataContext.Should().NotContainKey("CancellationReason",
            "subject values are placed raw, and this is text the canceller typed");
    }

    [Fact]
    public async Task A_withdrawn_cancellation_states_no_effective_date()
    {
        var lifecycleEvent = Event(SubscriptionConstants.SubscriptionCancellationWithdrawn);
        lifecycleEvent.CurrentPeriodEndUtc = null;

        await Service().SendAsync(lifecycleEvent, CancellationToken.None);

        _queued!.Payload.Purpose.Should().Be(SubscriptionConstants.CancellationWithdrawnMailPurpose);
        _queued.Payload.BodyDataContext["EffectiveDate"].Should().BeEmpty(
            "nothing is ending any more, so there is no date to state");
    }

    [Fact]
    public async Task A_billing_account_with_no_email_sends_nothing_and_records_why()
    {
        _accounts
            .Setup(repository => repository.GetAsync(
                "tenant-1", "account-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount());

        await Service().SendAsync(
            Event(SubscriptionConstants.SubscriptionCanceled), CancellationToken.None);

        _messages.Verify(
            client => client.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()),
            Times.Never,
            "a mail with no recipient would be rejected by the mail module after being acknowledged");
        _recorded.Should().ContainSingle().Which.Should().Match<MailDeliveryReportRequest>(report =>
            report.Outcome == MailDeliveryReportOutcome.NotAttempted &&
            report.ErrorCode == "billing_email_missing");
    }

    [Theory]
    [InlineData(SubscriptionConstants.UsageThresholdReached)]
    [InlineData(SubscriptionConstants.SubscriptionRenewed)]
    public async Task Events_that_are_not_notifications_are_ignored(string eventType)
    {
        await Service().SendAsync(Event(eventType), CancellationToken.None);

        _subscriptions.VerifyNoOtherCalls();
        _messages.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_member_given_a_seat_is_mailed_in_their_own_language()
    {
        Seat(releasedAtUtc: null);

        await Service().SendAsync(MemberEvent(SubscriptionConstants.SubscriptionMemberAssigned), CancellationToken.None);

        _queued.Should().NotBeNull();
        _queued!.Payload.Purpose.Should().Be(SubscriptionConstants.MemberAssignedMailPurpose);
        _queued.Payload.To.Should().Equal(["member@example.com"],
            "this email is the member's, not the billing contact's");
        _queued.Payload.Language.Should().Be("de-CH");
        _queued.Payload.BodyDataContext.Should().Equal(new Dictionary<string, string>
        {
            ["DisplayName"] = "Charles Babbage",
            ["PlanName"] = "Team",
            ["PlanCode"] = "team",
            ["OrganizationName"] = "Analytical Engines Ltd",
            ["ActorName"] = "Grace Hopper"
        });
        _accounts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_member_whose_seat_was_never_written_is_not_told_they_have_one()
    {
        // The event went in, then the process died before the seat did.
        _assignments
            .Setup(repository => repository.GetByIdAsync("tenant-1", "assignment-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionAssignment?)null);

        await Service().SendAsync(MemberEvent(SubscriptionConstants.SubscriptionMemberAssigned), CancellationToken.None);

        _queued.Should().BeNull("telling someone they have access they do not have is worse than silence");
        _recorded.Should().ContainSingle().Which.ErrorCode.Should().Be("seat_change_not_applied");
    }

    [Fact]
    public async Task A_member_whose_release_never_landed_is_not_told_they_lost_their_seat()
    {
        Seat(releasedAtUtc: null);

        await Service().SendAsync(MemberEvent(SubscriptionConstants.SubscriptionMemberReleased), CancellationToken.None);

        _queued.Should().BeNull("the seat is still held, so the member has lost nothing");
    }

    [Fact]
    public async Task A_member_whose_release_landed_is_told()
    {
        Seat(releasedAtUtc: new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc));

        await Service().SendAsync(MemberEvent(SubscriptionConstants.SubscriptionMemberReleased), CancellationToken.None);

        _queued!.Payload.Purpose.Should().Be(SubscriptionConstants.MemberRemovedMailPurpose);
    }

    [Fact]
    public async Task A_member_iam_gave_no_address_for_is_recorded_as_not_mailed()
    {
        Seat(releasedAtUtc: null);
        var lifecycleEvent = MemberEvent(SubscriptionConstants.SubscriptionMemberAssigned);
        lifecycleEvent.MemberEmail = null;

        await Service().SendAsync(lifecycleEvent, CancellationToken.None);

        _queued.Should().BeNull();
        _recorded.Should().ContainSingle().Which.ErrorCode.Should().Be("member_email_missing");
    }

    private void Seat(DateTime? releasedAtUtc) =>
        _assignments
            .Setup(repository => repository.GetByIdAsync("tenant-1", "assignment-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionAssignment { ItemId = "assignment-1", ReleasedAtUtc = releasedAtUtc });

    private static SubscriptionLifecycleEvent MemberEvent(string eventType)
    {
        var lifecycleEvent = Event(eventType);
        lifecycleEvent.AssignmentId = "assignment-1";
        lifecycleEvent.MemberUserId = "user-b";
        lifecycleEvent.MemberEmail = "Member@Example.com";
        lifecycleEvent.MemberDisplayName = "Charles Babbage";
        lifecycleEvent.MemberLanguage = "de-CH";
        lifecycleEvent.OrganizationName = "Analytical Engines Ltd";
        lifecycleEvent.ActorName = "Grace Hopper";
        return lifecycleEvent;
    }

    private SubscriptionNotificationEmailService Service() => new(
        _subscriptions.Object,
        _accounts.Object,
        _messages.Object,
        NullLogger<SubscriptionNotificationEmailService>.Instance,
        _reports.Object,
        _assignments.Object);

    private static SubscriptionDetail Subscription(string timeZoneId = "Europe/Zurich") => new()
    {
        ItemId = "subscription-1",
        TenantId = "tenant-1",
        OrganizationId = "organization-1",
        BillingAccountId = "account-1",
        Plan = new PlanSnapshot { Code = "team", DisplayName = "Team" },
        FeeSchedule = new BillingSchedule { TimeZoneId = timeZoneId }
    };

    private static SubscriptionLifecycleEvent Event(string eventType) => new()
    {
        EventId = "event-1",
        EventType = eventType,
        TenantId = "tenant-1",
        OrganizationId = "organization-1",
        SubscriptionId = "subscription-1",
        PlanCode = "team",
        PlanName = "Team",
        OccurredAtUtc = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc)
    };
}
