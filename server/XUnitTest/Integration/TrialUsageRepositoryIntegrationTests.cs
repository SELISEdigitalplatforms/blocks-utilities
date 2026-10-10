using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// "One trial per plan", proven against a real MongoDB.
/// </summary>
/// <remarks>
/// Guards against a subscriber getting a plan's trial twice — by racing two signups, or by
/// cancelling and starting again. Not mocked for the same reason the campaign ledger is not: the
/// risk sits in what the unique index actually refuses, which a mock cannot represent.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class TrialUsageRepositoryIntegrationTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);

    private readonly MongoIntegrationFixture _fixture;

    public TrialUsageRepositoryIntegrationTests(MongoIntegrationFixture fixture) => _fixture = fixture;

    private TrialUsageRepository Repository() => new(_fixture.DbContextProvider);

    [Fact]
    public async Task Signups_racing_for_the_same_trial_converge_on_exactly_one_winner()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();

        var attempts = Enumerable.Range(0, 20)
            .Select(_ => repository.TryReserveAsync(
                Usage(tenantId, Guid.NewGuid().ToString()), CancellationToken.None))
            .ToArray();

        var outcomes = await Task.WhenAll(attempts);

        outcomes.Count(won => won).Should().Be(1, "only one of the racing signups may start on the trial");
    }

    [Fact]
    public async Task A_retry_of_the_same_signup_keeps_its_claim()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();

        (await repository.TryReserveAsync(Usage(tenantId, "sub-1"), CancellationToken.None)).Should().BeTrue();
        (await repository.TryReserveAsync(Usage(tenantId, "sub-1"), CancellationToken.None))
            .Should().BeTrue("a retried signup must not be told someone else took its own trial");
    }

    [Fact]
    public async Task A_released_claim_frees_the_trial_and_keeps_its_history()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();
        var abandoned = Usage(tenantId, "abandoned");
        await repository.TryReserveAsync(abandoned, CancellationToken.None);

        await repository.ReleaseAsync(tenantId, abandoned.ItemId, Now, CancellationToken.None);

        (await repository.TryReserveAsync(Usage(tenantId, "next"), CancellationToken.None))
            .Should().BeTrue("a checkout that was never finished must not spend the trial");
        var active = await repository.FindActiveAsync(
            tenantId, SubscriberScope.Organization, "org-1", "pro", CancellationToken.None);
        active!.SubscriptionId.Should().Be("next");
    }

    [Fact]
    public async Task A_used_trial_is_never_released()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();
        var usage = Usage(tenantId, "sub-1");
        await repository.TryReserveAsync(usage, CancellationToken.None);
        await repository.MarkUsedAsync(Usage(tenantId, "sub-1"), Now, CancellationToken.None);

        await repository.ReleaseAsync(tenantId, usage.ItemId, Now, CancellationToken.None);

        var active = await repository.FindActiveAsync(
            tenantId, SubscriberScope.Organization, "org-1", "pro", CancellationToken.None);
        active!.State.Should().Be(TrialUsageState.Used,
            "a trial that started is spent even after the subscription is cancelled");
    }

    [Fact]
    public async Task Marking_used_without_a_claim_records_one_and_is_idempotent()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();

        await repository.MarkUsedAsync(Usage(tenantId, "sub-1"), Now, CancellationToken.None);
        await repository.MarkUsedAsync(Usage(tenantId, "sub-1"), Now, CancellationToken.None);

        var active = await repository.FindActiveAsync(
            tenantId, SubscriberScope.Organization, "org-1", "pro", CancellationToken.None);
        active!.State.Should().Be(TrialUsageState.Used,
            "a signup that crashed before claiming still spends its trial when the trial starts");
        (await repository.TryReserveAsync(Usage(tenantId, "sub-2"), CancellationToken.None))
            .Should().BeFalse("the recorded trial must block the next signup");
    }

    [Fact]
    public async Task A_different_plan_or_subscriber_has_its_own_trial()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var repository = Repository();
        await repository.MarkUsedAsync(Usage(tenantId, "sub-1"), Now, CancellationToken.None);

        var otherPlan = Usage(tenantId, "sub-2");
        otherPlan.PlanCode = "basic";
        var otherOrganization = Usage(tenantId, "sub-3");
        otherOrganization.SubjectId = "org-2";

        (await repository.TryReserveAsync(otherPlan, CancellationToken.None))
            .Should().BeTrue("trialing one plan must not block trialing another");
        (await repository.TryReserveAsync(otherOrganization, CancellationToken.None))
            .Should().BeTrue("one organization's trial is not another's");
    }

    private static TrialUsage Usage(string tenantId, string subscriptionId) => new()
    {
        TenantId = tenantId,
        Scope = SubscriberScope.Organization,
        SubjectId = "org-1",
        PlanCode = "pro",
        OrganizationId = "org-1",
        SubscriptionId = subscriptionId,
        ClaimedAtUtc = Now
    };
}
