using System.Text.Json;
using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// What the seat, plan and cancellation events carry for the notification emails (spec 001).
/// </summary>
/// <remarks>
/// Guards against an email that misstates the change: seats listed that did not move, a removed
/// item left out, or a cancellation that forgets who asked for it and why.
/// </remarks>
public sealed class SubscriptionOutboxEventFactoryTests
{
    private readonly SubscriptionOutboxEventFactory _factory = new();

    [Fact]
    public void A_quantity_change_lists_only_the_items_that_moved_including_ones_added_or_removed()
    {
        var previous = new List<SubscriptionQuantityItem>
        {
            Item("user", 5),
            Item("storage", 100),
            Item("project", 2)
        };
        var current = new List<SubscriptionQuantityItem>
        {
            // Reordered on purpose: the two lists come from different writers.
            Item("project", 2),
            Item("user", 8),
            Item("api", 1)
        };

        var payload = Payload(_factory.CreateQuantityChanged(
            Subscription(), previous, current, "Grace Hopper", "corr-1"));

        payload.QuantityChanges.Should().BeEquivalentTo(
            [
                new LifecycleQuantityChange { ItemKey = "user", UnitLabel = "user", PreviousQuantity = 5, Quantity = 8 },
                new LifecycleQuantityChange { ItemKey = "api", UnitLabel = "api", PreviousQuantity = 0, Quantity = 1 },
                new LifecycleQuantityChange { ItemKey = "storage", UnitLabel = "storage", PreviousQuantity = 100, Quantity = 0 }
            ],
            "an unchanged item is noise in the email, and an item dropped entirely is a change too");
        payload.ActorName.Should().Be("Grace Hopper");
        payload.PlanName.Should().Be("Team");
    }

    [Fact]
    public void A_plan_change_carries_the_name_of_the_plan_being_left()
    {
        var payload = Payload(_factory.CreatePlanChanged(
            Subscription(), "starter", "Starter", "Grace Hopper", "corr-1"));

        payload.PlanCode.Should().Be("team");
        payload.PreviousPlanCode.Should().Be("starter");
        payload.PreviousPlanName.Should().Be("Starter",
            "the plan may be renamed before the mail is sent; the customer left it as it was called");
        payload.ActorName.Should().Be("Grace Hopper");
    }

    [Fact]
    public void A_cancellation_carries_its_reason_and_who_asked()
    {
        var endsAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        var payload = Payload(_factory.CreateCancellation(
            Subscription(),
            SubscriptionConstants.SubscriptionCancellationRequested,
            cancelAtPeriodEnd: true,
            endsAt,
            "too expensive",
            "Grace Hopper",
            "corr-1"));

        payload.CancellationReason.Should().Be("too expensive");
        payload.ActorName.Should().Be("Grace Hopper");
        payload.CurrentPeriodEndUtc.Should().Be(endsAt);
    }

    private static SubscriptionDetail Subscription() => new()
    {
        ItemId = "sub-1",
        TenantId = "tenant-1",
        OrganizationId = "org-1",
        Version = 3,
        Plan = new PlanSnapshot { Code = "team", DisplayName = "Team" }
    };

    private static SubscriptionQuantityItem Item(string key, long quantity) => new()
    {
        ItemKey = key,
        UnitLabel = key,
        Quantity = quantity
    };

    private static SubscriptionLifecycleEvent Payload(SubscriptionOutboxEvent outboxEvent) =>
        JsonSerializer.Deserialize<SubscriptionLifecycleEvent>(
            outboxEvent.Payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}
