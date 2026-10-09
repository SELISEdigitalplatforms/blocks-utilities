using FluentAssertions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Subscription;

/// <summary>
/// Whose trial a subscription spends.
/// </summary>
/// <remarks>
/// Guards against a subscriber cancelling and signing up again for a second trial, and against the
/// opposite mistake: one person's trial of a user-wise plan blocking a colleague's.
/// </remarks>
public sealed class TrialUsageTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_organization_wise_plans_trial_belongs_to_the_organization()
    {
        var usage = TrialUsage.For(Subscription("user-1"), SubscriberScope.Organization, "pro", Now);

        usage!.SubjectId.Should().Be("org-1",
            "a different member signing up the same organization must not get a second trial");
        usage.PlanCode.Should().Be("pro");
    }

    [Fact]
    public void A_user_wise_plans_trial_belongs_to_the_buyer()
    {
        var usage = TrialUsage.For(Subscription("user-1"), SubscriberScope.User, "pro", Now);

        usage!.SubjectId.Should().Be("user-1",
            "a user-wise plan is one person's allowance, so its trial is too");
    }

    [Fact]
    public void A_user_wise_signup_without_a_buyer_claims_nothing()
    {
        TrialUsage.For(Subscription(null), SubscriberScope.User, "pro", Now).Should().BeNull(
            "every user-less signup would otherwise share one slot and only the first would get a trial");
    }

    [Fact]
    public async Task Marking_a_trial_started_without_a_ledger_does_nothing()
    {
        ITrialUsageRepository? none = null;

        var act = () => none.MarkTrialStartedAsync(Subscription("user-1"), Now, CancellationToken.None);

        await act.Should().NotThrowAsync("hosts and tests built without the ledger must behave as before");
    }

    [Fact]
    public async Task Marking_a_trial_started_records_the_subscriptions_current_plan()
    {
        var trials = new Mock<ITrialUsageRepository>();

        await trials.Object.MarkTrialStartedAsync(Subscription("user-1"), Now, CancellationToken.None);

        trials.Verify(
            repository => repository.MarkUsedAsync(
                It.Is<TrialUsage>(usage =>
                    usage.SubscriptionId == "sub-1" && usage.PlanCode == "pro" && usage.SubjectId == "org-1"),
                Now,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the trial is spent when it starts, so cancelling straight after cannot buy it back");
    }

    private static SubscriptionDetail Subscription(string? buyerUserId) => new()
    {
        ItemId = "sub-1",
        TenantId = "tenant-1",
        OrganizationId = "org-1",
        PurchasedBy = buyerUserId is null ? null : new FinancialDocumentPerson { UserId = buyerUserId },
        Plan = new PlanSnapshot { Code = "pro" }
    };
}
