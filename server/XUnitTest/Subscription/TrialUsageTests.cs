using FluentAssertions;
using MongoDB.Bson.Serialization;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Subscription;

/// <summary>
/// Whose trial a subscription spends.
/// </summary>
/// <remarks>
/// Guards against an organization getting a second trial by cancelling and signing up again, or by
/// having a colleague buy it instead, and against the opposite mistake: a person's trial in one
/// organization refusing them the trial for another.
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
    public void A_user_wise_plans_trial_belongs_to_the_organization_not_the_buyer()
    {
        var usage = TrialUsage.For(Subscription("user-1"), SubscriberScope.User, "pro", Now);

        usage!.SubjectId.Should().Be("org-1",
            "keyed on the buyer, an organization could trial the plan again just by switching who buys it");
        usage.Scope.Should().Be(SubscriberScope.User);
    }

    [Fact]
    public void A_user_wise_signup_without_a_buyer_claims_for_its_organization()
    {
        TrialUsage.For(Subscription(null), SubscriberScope.User, "pro", Now)!.SubjectId.Should().Be("org-1",
            "an API key signing up must not be a way round the organization's one trial");
    }

    [Fact]
    public void A_subscription_with_no_organization_claims_nothing()
    {
        var subscription = Subscription("user-1");
        subscription.OrganizationId = string.Empty;

        TrialUsage.For(subscription, SubscriberScope.Organization, "pro", Now).Should().BeNull(
            "every such signup would otherwise share one empty-id slot and only the first would get a trial");
    }

    [Fact]
    public async Task Starting_the_trial_of_a_plan_sold_with_repeat_trials_records_nothing()
    {
        var trials = new Mock<ITrialUsageRepository>();
        var subscription = Subscription("user-1");
        subscription.Plan.OneTrialPerPlan = false;

        await trials.Object.MarkTrialStartedAsync(subscription, Now, CancellationToken.None);

        trials.Verify(
            repository => repository.MarkUsedAsync(
                It.IsAny<TrialUsage>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a claim left behind would refuse the trial again once the plan is switched back on");
    }

    [Fact]
    public void A_plan_authored_before_the_setting_existed_keeps_one_trial_per_plan()
    {
        var plan = BsonSerializer.Deserialize<Plan>("{ Code: 'pro' }");
        var snapshot = BsonSerializer.Deserialize<PlanSnapshot>("{ Code: 'pro' }");

        plan.OneTrialPerPlan.Should().BeTrue(
            "read as false, every existing plan would lose its trial limit the day this shipped");
        snapshot.OneTrialPerPlan.Should().BeTrue(
            "a subscription sold before the setting existed must still spend its trial when it starts");
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
