using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The guarantees this module leans on that only a real database can demonstrate.
/// </summary>
/// <remarks>
/// Uniqueness, atomic increments and compare-and-set are all enforced by MongoDB, not by our
/// code. Mocking them would test the mock: every one of these would pass against an in-memory
/// stand-in while the real collection happily wrote a second live subscription or counted one
/// screening twice.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriptionRepositoryIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionRepository _subscriptions;
    private readonly SubscriptionUsageRepository _usage;
    private readonly SubscriptionPaymentLinkRepository _links;
    private readonly BillingAccountRepository _accounts;

    public SubscriptionRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _subscriptions = new SubscriptionRepository(fixture.DbContextProvider);
        _usage = new SubscriptionUsageRepository(fixture.DbContextProvider);
        _links = new SubscriptionPaymentLinkRepository(fixture.DbContextProvider);
        _accounts = new BillingAccountRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task An_active_subscription_blocks_a_new_signup_before_checkout()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _subscriptions.TryCreateAsync(
            NewSubscription(tenantId, "org-1", SubscriptionStatus.Active),
            CancellationToken.None)).Should().BeTrue();

        (await _subscriptions.TryCreateAsync(
                NewSubscription(tenantId, "org-1", SubscriptionStatus.Incomplete),
                CancellationToken.None))
            .Should().BeFalse("the incomplete row reserves checkout, so the customer cannot " +
                              "pay before discovering that the organization is already subscribed");
    }

    [Fact]
    public async Task Only_one_of_two_concurrent_signups_reaches_checkout()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var outcomes = await Task.WhenAll(
            _subscriptions.TryCreateAsync(
                NewSubscription(tenantId, "org-concurrent", SubscriptionStatus.Incomplete),
                CancellationToken.None),
            _subscriptions.TryCreateAsync(
                NewSubscription(tenantId, "org-concurrent", SubscriptionStatus.Incomplete),
                CancellationToken.None));

        outcomes.Count(created => created).Should().Be(1,
            "the database reservation, rather than a pre-read, decides which signup may charge");
    }

    [Fact]
    public async Task An_ended_subscription_does_not_block_resubscribing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _subscriptions.TryCreateAsync(
            NewSubscription(tenantId, "org-2", SubscriptionStatus.Canceled),
            CancellationToken.None);

        (await _subscriptions.TryCreateAsync(
                NewSubscription(tenantId, "org-2", SubscriptionStatus.Active),
                CancellationToken.None))
            .Should().BeTrue("ended states release the signup reservation");
    }

    [Fact]
    public async Task Only_one_of_two_concurrent_transitions_wins()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = NewSubscription(tenantId, "org-3", SubscriptionStatus.Incomplete);

        await _subscriptions.TryCreateAsync(
            subscription,
            CancellationToken.None);

        var transition = new SubscriptionTransition(
            SubscriptionStatus.Incomplete,
            SubscriptionStatus.Active)
        {
            ActivatedAtUtc = DateTime.UtcNow
        };

        var first = _subscriptions.TryTransitionAsync(
            tenantId,
            subscription.ItemId,
            transition,
            CancellationToken.None);

        var second = _subscriptions.TryTransitionAsync(
            tenantId,
            subscription.ItemId,
            transition,
            CancellationToken.None);

        var outcomes = await Task.WhenAll(first, second);

        outcomes.Count(succeeded => succeeded).Should().Be(1,
            "activation must apply once even if two sweeps pick up the same payment");

        var stored = await _subscriptions.GetByIdAsync(
            tenantId,
            subscription.ItemId,
            CancellationToken.None);

        stored!.Status.Should().Be(SubscriptionStatus.Active);
        stored.Version.Should().Be(2);
    }

    [Fact]
    public async Task A_scheduled_cancellation_persists_whether_it_may_be_escalated()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = NewSubscription(tenantId, "org-cancel", SubscriptionStatus.Active);

        await _subscriptions.TryCreateAsync(subscription, CancellationToken.None);

        (await _subscriptions.TryTransitionAsync(
            tenantId,
            subscription.ItemId,
            new SubscriptionTransition(SubscriptionStatus.Active, SubscriptionStatus.Active)
            {
                CancelAtPeriodEnd = true,
                CanCancelImmediately = true,
                CanceledAtUtc = DateTime.UtcNow,
                RequireCancellationNotAlreadyScheduled = true
            },
            CancellationToken.None)).Should().BeTrue();

        var stored = await _subscriptions.GetByIdAsync(
            tenantId, subscription.ItemId, CancellationToken.None);

        stored!.CancelAtPeriodEnd.Should().BeTrue();
        stored.CanCancelImmediately.Should().BeTrue(
            "an ordinary period-end cancellation must record that it may later be escalated");
    }

    /// <summary>
    /// Only the real collection's compare-and-set can show that a duplicate cancellation loses the
    /// race but still converges on the same schedule the winner wrote — a mock would just report
    /// whatever it was told to.
    /// </summary>
    [Fact]
    public async Task Two_concurrent_period_end_cancellations_converge_on_one_schedule()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = NewSubscription(tenantId, "org-cancel-race", SubscriptionStatus.Active);

        await _subscriptions.TryCreateAsync(subscription, CancellationToken.None);

        var transition = new SubscriptionTransition(SubscriptionStatus.Active, SubscriptionStatus.Active)
        {
            CancelAtPeriodEnd = true,
            CanCancelImmediately = true,
            CanceledAtUtc = DateTime.UtcNow,
            RequireCancellationNotAlreadyScheduled = true
        };

        var outcomes = await Task.WhenAll(
            _subscriptions.TryTransitionAsync(
                tenantId, subscription.ItemId, transition, CancellationToken.None),
            _subscriptions.TryTransitionAsync(
                tenantId, subscription.ItemId, transition, CancellationToken.None));

        outcomes.Count(succeeded => succeeded).Should().Be(1,
            "only one write should actually happen; the loser's caller converges on it instead");

        var stored = await _subscriptions.GetByIdAsync(
            tenantId, subscription.ItemId, CancellationToken.None);

        stored!.CancelAtPeriodEnd.Should().BeTrue();
        stored.Version.Should().Be(2, "a lost duplicate must not bump the version a second time");
    }

    [Fact]
    public async Task Only_a_scheduled_cancellation_past_its_period_end_is_due()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var notYetDue = NewSubscription(tenantId, "org-not-due", SubscriptionStatus.Active);
        notYetDue.CurrentPeriodEndUtc = DateTime.UtcNow.AddDays(1);
        var due = NewSubscription(tenantId, "org-due", SubscriptionStatus.Active);
        due.CurrentPeriodEndUtc = DateTime.UtcNow.AddMinutes(-1);
        var unscheduled = NewSubscription(tenantId, "org-unscheduled", SubscriptionStatus.Active);
        unscheduled.CurrentPeriodEndUtc = DateTime.UtcNow.AddMinutes(-1);

        await _subscriptions.TryCreateAsync(notYetDue, CancellationToken.None);
        await _subscriptions.TryCreateAsync(due, CancellationToken.None);
        await _subscriptions.TryCreateAsync(unscheduled, CancellationToken.None);

        foreach (var subscription in new[] { notYetDue, due })
        {
            await _subscriptions.TryTransitionAsync(
                tenantId,
                subscription.ItemId,
                new SubscriptionTransition(SubscriptionStatus.Active, SubscriptionStatus.Active)
                {
                    CancelAtPeriodEnd = true,
                    CanCancelImmediately = true,
                    CanceledAtUtc = DateTime.UtcNow,
                    RequireCancellationNotAlreadyScheduled = true
                },
                CancellationToken.None);
        }

        var found = await _subscriptions.ListDueForCancellationAsync(
            tenantId, DateTime.UtcNow, 10, CancellationToken.None);

        found.Select(subscription => subscription.ItemId).Should().BeEquivalentTo([due.ItemId],
            "the one not yet at its period end, and the one with no cancellation scheduled at " +
            "all, must not show up here");
    }

    /// <summary>
    /// A real database is the only thing that can demonstrate this: a mocked repository would
    /// answer with whatever the test told it to, which proves nothing about the actual filter.
    /// </summary>
    [Fact]
    public async Task A_scheduled_cancellation_stops_being_live_the_instant_its_boundary_passes()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = NewSubscription(tenantId, "org-boundary", SubscriptionStatus.Active);
        subscription.CurrentPeriodEndUtc = DateTime.UtcNow.AddSeconds(1);

        await _subscriptions.TryCreateAsync(subscription, CancellationToken.None);
        await _subscriptions.TryTransitionAsync(
            tenantId,
            subscription.ItemId,
            new SubscriptionTransition(SubscriptionStatus.Active, SubscriptionStatus.Active)
            {
                CancelAtPeriodEnd = true,
                CanCancelImmediately = true,
                CanceledAtUtc = DateTime.UtcNow,
                RequireCancellationNotAlreadyScheduled = true
            },
            CancellationToken.None);

        (await _subscriptions.GetLiveAsync(
                tenantId, "org-boundary", DateTime.UtcNow, CancellationToken.None))
            .Should().NotBeNull("still inside the paid period");

        (await _subscriptions.GetLiveAsync(
                tenantId, "org-boundary", subscription.CurrentPeriodEndUtc, CancellationToken.None))
            .Should().BeNull(
                "the promised boundary has arrived, whether or not the finalizing worker has " +
                "run yet — Status here is still Active");
    }

    /// <summary>
    /// A subscription that never scheduled a cancellation at all must not be affected by the same
    /// filter clause — only <see cref="SubscriptionDetail.CancelAtPeriodEnd"/> subscriptions are
    /// ever compared against the boundary.
    /// </summary>
    [Fact]
    public async Task A_subscription_with_no_scheduled_cancellation_stays_live_indefinitely()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = NewSubscription(tenantId, "org-no-schedule", SubscriptionStatus.Active);
        subscription.CurrentPeriodEndUtc = DateTime.UtcNow.AddSeconds(-1);

        await _subscriptions.TryCreateAsync(subscription, CancellationToken.None);

        (await _subscriptions.GetLiveAsync(
                tenantId, "org-no-schedule", DateTime.UtcNow, CancellationToken.None))
            .Should().NotBeNull(
                "CurrentPeriodEndUtc having passed means nothing on its own — only a scheduled " +
                "cancellation makes it a boundary that stops entitlement");
    }

    [Fact]
    public async Task A_canceled_subscriptions_queued_final_window_still_shows_up_for_rating()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var canceledWithoutUsage = NewSubscription(
            tenantId, "org-canceled-no-usage", SubscriptionStatus.Active);
        var canceledWithUsage = NewSubscription(
            tenantId, "org-canceled-usage", SubscriptionStatus.Active);

        await _subscriptions.TryCreateAsync(canceledWithoutUsage, CancellationToken.None);
        await _subscriptions.TryCreateAsync(canceledWithUsage, CancellationToken.None);

        await _subscriptions.TryTransitionAsync(
            tenantId,
            canceledWithoutUsage.ItemId,
            new SubscriptionTransition(SubscriptionStatus.Active, SubscriptionStatus.Canceled)
            {
                EndedAtUtc = DateTime.UtcNow,
                ClearNextUsageBillingAt = true
            },
            CancellationToken.None);

        await _subscriptions.TryTransitionAsync(
            tenantId,
            canceledWithUsage.ItemId,
            new SubscriptionTransition(SubscriptionStatus.Active, SubscriptionStatus.Canceled)
            {
                EndedAtUtc = DateTime.UtcNow,
                ClearNextUsageBillingAt = true,
                OutgoingUsagePeriod = new PendingUsagePeriod
                {
                    PeriodKey = "M20260801T000000Z",
                    Plan = canceledWithUsage.Plan,
                    Price = canceledWithUsage.Price,
                    CurrencyCode = "CHF",
                    CorrelationId = "cancel-1"
                }
            },
            CancellationToken.None);

        var found = await _subscriptions.ListDueForUsageRatingAsync(
            tenantId, DateTime.UtcNow.AddDays(1), 10, CancellationToken.None);

        found.Select(subscription => subscription.ItemId)
            .Should().NotContain(canceledWithoutUsage.ItemId,
                "nothing is left to rate once a cancellation with no queued window ends");
        found.Select(subscription => subscription.ItemId)
            .Should().Contain(canceledWithUsage.ItemId,
                "its final window is still unrated, and nothing else will ever look at it again " +
                "once it has left the live statuses");
    }

    [Fact]
    public async Task The_same_usage_key_can_only_be_recorded_once()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _usage.TryAppendRecordAsync(
            NewUsageRecord(tenantId, "sub-1", "key-1"),
            CancellationToken.None)).Should().BeTrue();

        (await _usage.TryAppendRecordAsync(
                NewUsageRecord(tenantId, "sub-1", "key-1"),
                CancellationToken.None))
            .Should().BeFalse("a retried call must not become a second billable event");
    }

    [Fact]
    public async Task A_counter_returns_the_balance_including_the_callers_own_delta()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var seed = NewCounter(tenantId, "sub-2", "screening", "M20260801T000000Z");

        var first = await _usage.ApplyDeltaAsync(
            seed,
            1,
            CancellationToken.None);

        var second = await _usage.ApplyDeltaAsync(
            seed,
            1,
            CancellationToken.None);

        first.Balance.Should().Be(1);
        second.Balance.Should().Be(2, "the post-increment balance is what makes recording an " +
                                      "enforcement point rather than a check");
        second.AppliedRecordCount.Should().Be(2);
        second.LimitSnapshot.Should().Be(500, "the allowance is captured when the period opens");
    }

    [Fact]
    public async Task Concurrent_increments_all_land()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var seed = NewCounter(tenantId, "sub-3", "screening", "M20260801T000000Z");

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            _usage.ApplyDeltaAsync(seed, 1, CancellationToken.None)));

        var counter = await _usage.GetCounterAsync(
            tenantId,
            seed.ItemId,
            CancellationToken.None);

        counter!.Balance.Should().Be(20, "a read-modify-write would lose some of these");
    }

    [Fact]
    public async Task Adjacent_periods_are_separate_counters()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _usage.ApplyDeltaAsync(
            NewCounter(tenantId, "sub-4", "screening", "M20260801T000000Z"),
            5,
            CancellationToken.None);

        var next = await _usage.ApplyDeltaAsync(
            NewCounter(tenantId, "sub-4", "screening", "M20260901T000000Z"),
            1,
            CancellationToken.None);

        next.Balance.Should().Be(1, "a new period starts at zero without any rollover job");
    }

    [Fact]
    public async Task A_threshold_is_reported_by_exactly_one_caller()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var seed = NewCounter(tenantId, "sub-5", "screening", "M20260801T000000Z");

        await _usage.ApplyDeltaAsync(seed, 400, CancellationToken.None);

        var outcomes = await Task.WhenAll(
            _usage.TryMarkThresholdNotifiedAsync(
                tenantId, seed.ItemId, 80, CancellationToken.None),
            _usage.TryMarkThresholdNotifiedAsync(
                tenantId, seed.ItemId, 80, CancellationToken.None));

        outcomes.Count(won => won).Should().Be(1,
            "otherwise every screening past the threshold sends another email");
    }

    [Fact]
    public async Task A_payment_can_be_linked_to_only_one_subscription()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _links.TryCreateAsync(
            NewLink(tenantId, "sub-6", "pay-1"),
            CancellationToken.None)).Should().BeTrue();

        (await _links.TryCreateAsync(
                NewLink(tenantId, "sub-7", "pay-1"),
                CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task A_link_settles_once_however_many_sweeps_see_it()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var link = NewLink(tenantId, "sub-8", "pay-2");

        await _links.TryCreateAsync(link, CancellationToken.None);

        var outcomes = await Task.WhenAll(
            _links.TrySettleAsync(
                tenantId,
                link.ItemId,
                SubscriptionPaymentLinkState.Applied,
                CancellationToken.None),
            _links.TrySettleAsync(
                tenantId,
                link.ItemId,
                SubscriptionPaymentLinkState.Applied,
                CancellationToken.None));

        outcomes.Count(settled => settled).Should().Be(1);
    }

    [Fact]
    public async Task A_billing_account_is_created_once_per_organization_and_provider()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var first = await _accounts.GetOrCreateAndReconcileAsync(
            NewAccount(tenantId, "org-9"),
            CancellationToken.None);

        var second = await _accounts.GetOrCreateAndReconcileAsync(
            NewAccount(tenantId, "org-9"),
            CancellationToken.None);

        second.ItemId.Should().Be(first.ItemId);
    }

    /// <summary>
    /// The reason this operation reconciles rather than merely creating.
    /// </summary>
    /// <remarks>
    /// An account is one per organization and provider and outlives every subscription on it, so an
    /// organization that subscribed before filling its billing profile in had a blank contact stored
    /// for good. Fixing the profile and subscribing again returned the old account untouched, and
    /// renewal and usage-threshold mail went on going nowhere.
    /// <para>
    /// Only a real collection shows it. A mock returning the account it was handed reports success
    /// for the very write that never happened, which is exactly what the unit tests did.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_billing_account_takes_up_a_contact_it_was_created_without()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var created = await _accounts.GetOrCreateAndReconcileAsync(
            NewAccount(tenantId, "org-contact-1"),
            CancellationToken.None);

        created.BillingEmail.Should().BeNull();

        var account = NewAccount(tenantId, "org-contact-1");
        account.BillingEmail = "billing@northwind.example";
        account.BillingName = "Ada Byron";

        var reconciled = await _accounts.GetOrCreateAndReconcileAsync(
            account,
            CancellationToken.None);

        // The same account, now reachable. A new one would strand the subscriptions pointing at the
        // first, so the id has to survive the reconciliation.
        reconciled.ItemId.Should().Be(created.ItemId);
        reconciled.BillingEmail.Should().Be("billing@northwind.example");
        reconciled.BillingName.Should().Be("Ada Byron");
        reconciled.CreatedAtUtc.Should().Be(created.CreatedAtUtc);
    }

    /// <summary>
    /// Everything else on the account survives being created through the reconciling upsert.
    /// </summary>
    /// <remarks>
    /// The upsert first written for this named its inserted fields by hand and left these two out, so
    /// an account created with a customer and a saved card arrived with neither. Nothing near the
    /// billing profile noticed: it surfaced two suites away, as a renewal that reached the provider
    /// with no card to present and never charged. Pinned here so the next field added to the entity
    /// cannot go the same way.
    /// </remarks>
    [Fact]
    public async Task Creating_an_account_keeps_the_fields_this_operation_does_not_reconcile()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var account = NewAccount(tenantId, "org-contact-5");
        account.ProviderCustomerId = "cus_123";
        account.DefaultPaymentMethodId = "pm-1";
        account.ProviderOrganizationId = "default";
        account.BillingEmail = "billing@northwind.example";

        var created = await _accounts.GetOrCreateAndReconcileAsync(
            account,
            CancellationToken.None);

        created.ProviderCustomerId.Should().Be("cus_123");
        created.DefaultPaymentMethodId.Should().Be("pm-1");
        created.ProviderOrganizationId.Should().Be("default");
        created.BillingEmail.Should().Be("billing@northwind.example");
        created.Version.Should().Be(1);

        // And read back from the collection, not just returned: an upsert that answers correctly
        // while storing less than it should is the failure this is about.
        var reloaded = await _accounts.GetAsync(
            tenantId,
            created.ItemId,
            CancellationToken.None);

        reloaded!.ProviderCustomerId.Should().Be("cus_123");
        reloaded.DefaultPaymentMethodId.Should().Be("pm-1");
        reloaded.ProviderOrganizationId.Should().Be("default");
    }

    [Fact]
    public async Task Reconciling_a_contact_leaves_the_provider_details_alone()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var account = NewAccount(tenantId, "org-contact-6");
        account.ProviderCustomerId = "cus_123";
        account.DefaultPaymentMethodId = "pm-1";
        var created = await _accounts.GetOrCreateAndReconcileAsync(
            account,
            CancellationToken.None);

        // A later signup knows the contact and nothing about the provider, which is exactly what the
        // creation service hands over: it builds an account from the billing profile alone.
        var later = NewAccount(tenantId, "org-contact-6");
        later.BillingEmail = "billing@northwind.example";

        var reconciled = await _accounts.GetOrCreateAndReconcileAsync(
            later,
            CancellationToken.None);

        reconciled.BillingEmail.Should().Be("billing@northwind.example");

        // The card and the customer are the account's standing with the provider and no business of
        // a contact update. Blanking them would leave a renewal unable to charge.
        reconciled.ItemId.Should().Be(created.ItemId);
        reconciled.ProviderCustomerId.Should().Be("cus_123");
        reconciled.DefaultPaymentMethodId.Should().Be("pm-1");
    }

    [Fact]
    public async Task A_billing_account_follows_a_contact_that_changed()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var first = NewAccount(tenantId, "org-contact-2");
        first.BillingEmail = "old@northwind.example";
        first.BillingName = "Ada Byron";
        await _accounts.GetOrCreateAndReconcileAsync(first, CancellationToken.None);

        var second = NewAccount(tenantId, "org-contact-2");
        second.BillingEmail = "new@northwind.example";
        second.BillingName = "Grace Hopper";

        var reconciled = await _accounts.GetOrCreateAndReconcileAsync(
            second,
            CancellationToken.None);

        // A stale address is the failure this exists for: mail kept going to whoever the profile
        // used to name, months after somebody corrected it.
        reconciled.BillingEmail.Should().Be("new@northwind.example");
        reconciled.BillingName.Should().Be("Grace Hopper");
        reconciled.Version.Should().BeGreaterThan(1, "a reconciliation is a change to the account");
    }

    [Fact]
    public async Task A_contact_this_caller_does_not_know_is_left_alone_rather_than_blanked()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var first = NewAccount(tenantId, "org-contact-3");
        first.BillingEmail = "billing@northwind.example";
        first.BillingName = "Ada Byron";
        var created = await _accounts.GetOrCreateAndReconcileAsync(first, CancellationToken.None);

        // Knows an address and no name, which is what a caller sending one field looks like.
        var second = NewAccount(tenantId, "org-contact-3");
        second.BillingEmail = "later@northwind.example";

        var reconciled = await _accounts.GetOrCreateAndReconcileAsync(
            second,
            CancellationToken.None);

        reconciled.BillingEmail.Should().Be("later@northwind.example");
        reconciled.BillingName.Should().Be(
            "Ada Byron",
            "a caller that named no name meant the address, and blanking it would lose the only " +
            "name there is");

        // And a call that knows neither leaves the account exactly as it stands, timestamp included.
        var untouched = await _accounts.GetOrCreateAndReconcileAsync(
            NewAccount(tenantId, "org-contact-3"),
            CancellationToken.None);

        untouched.BillingEmail.Should().Be("later@northwind.example");
        untouched.BillingName.Should().Be("Ada Byron");
        untouched.CreatedAtUtc.Should().Be(created.CreatedAtUtc);
    }

    [Fact]
    public async Task Concurrent_signups_converge_on_one_account_and_one_contact()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        // Sixteen at once, all reconciling to the same values, which is what a real signup burst
        // looks like: the profile is one answer and every request read it.
        var accounts = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
        {
            var account = NewAccount(tenantId, "org-contact-4");
            account.BillingEmail = "billing@northwind.example";
            account.BillingName = "Ada Byron";

            return _accounts.GetOrCreateAndReconcileAsync(account, CancellationToken.None);
        }));

        // One document, not sixteen. The upsert is keyed on the unique index, so a loser reads the
        // winner rather than inserting a second account the next renewal would have to choose
        // between.
        accounts.Select(account => account.ItemId).Distinct().Should().HaveCount(1);
        accounts.Should().OnlyContain(
            account => account.BillingEmail == "billing@northwind.example");
    }

    [Fact]
    public async Task A_provider_customer_that_changes_is_followed_and_reported()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var account = await _accounts.GetOrCreateAndReconcileAsync(
            NewAccount(tenantId, "org-10"),
            CancellationToken.None);

        (await _accounts.TrySetProviderCustomerAsync(
                tenantId, account.ItemId, "cus_first", "pm_1", "default",
                CancellationToken.None))
            .Should().Be(SetProviderCustomerOutcome.Recorded);

        (await _accounts.TrySetProviderCustomerAsync(
                tenantId, account.ItemId, "cus_first", "pm_1", "default",
                CancellationToken.None))
            .Should().Be(SetProviderCustomerOutcome.Unchanged);

        // Refusing here used to look like the careful choice. It left the account naming a
        // customer that no later payment writes to, and the card it pointed at unreachable —
        // which a renewal only discovers a whole billing period afterwards.
        (await _accounts.TrySetProviderCustomerAsync(
                tenantId, account.ItemId, "cus_other", "pm_2", "default",
                CancellationToken.None))
            .Should().Be(SetProviderCustomerOutcome.Repointed);

        var stored = await _accounts.GetAsync(
            tenantId,
            account.ItemId,
            CancellationToken.None);

        stored!.ProviderCustomerId.Should().Be("cus_other");
        stored.DefaultPaymentMethodId.Should().Be(
            "pm_2",
            "the card a renewal presents must be the one the latest charge actually saved");
    }

    [Fact]
    public async Task Recording_against_an_account_that_is_not_there_says_so()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _accounts.TrySetProviderCustomerAsync(
                tenantId, "missing", "cus_first", "pm_1", "default",
                CancellationToken.None))
            .Should().Be(SetProviderCustomerOutcome.AccountMissing);
    }

    /// <summary>
    /// Finding 2: every billing account created before this PR shipped has <c>ProviderId</c> and
    /// <c>ProviderOrganizationId</c> permanently null, because the ordinary reconcile path only
    /// ever touches the contact fields. Left unfixed, checkout's fail-closed
    /// <c>ExpectedProviderId</c> comparison skips itself on a null value, so none of those legacy
    /// accounts would ever get the provider-identity protection this PR chain built. The next
    /// reconcile call for such an account -- an ordinary subscription action, not a migration --
    /// must self-heal it by filling in the identity the caller now supplies.
    /// </summary>
    [Fact]
    public async Task A_legacy_account_with_no_provider_identity_is_backfilled_on_reconcile()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        // A pre-existing account, created before ProviderId/ProviderOrganizationId existed on
        // this entity -- both left null, exactly like a real legacy row.
        var legacy = NewAccount(tenantId, "org-legacy-1");
        var created = await _accounts.GetOrCreateAndReconcileAsync(legacy, CancellationToken.None);

        created.ProviderId.Should().BeNull();

        var touchedAgain = NewAccount(tenantId, "org-legacy-1");
        touchedAgain.ProviderId = "provider-row-1";
        touchedAgain.ProviderOrganizationId = "org-legacy-1";

        var backfilled = await _accounts.GetOrCreateAndReconcileAsync(
            touchedAgain,
            CancellationToken.None);

        backfilled.ItemId.Should().Be(created.ItemId);
        backfilled.ProviderId.Should().Be("provider-row-1");
        backfilled.ProviderOrganizationId.Should().Be("org-legacy-1");

        // And read back from the collection, not just returned: the backfill is a separate write
        // from the reconcile's own upsert, so only a reload proves it actually persisted.
        var reloaded = await _accounts.GetAsync(tenantId, created.ItemId, CancellationToken.None);

        reloaded!.ProviderId.Should().Be("provider-row-1");
        reloaded.ProviderOrganizationId.Should().Be("org-legacy-1");
    }

    /// <summary>
    /// The other half of Finding 2: a provider identity, once recorded, is frozen. An ordinary
    /// reconcile must never be the thing that silently moves a billing account onto a different
    /// provider row, even if a caller somehow supplies a different value.
    /// </summary>
    [Fact]
    public async Task An_already_frozen_provider_identity_is_never_overwritten_by_reconcile()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var first = NewAccount(tenantId, "org-legacy-2");
        first.ProviderId = "provider-row-original";
        first.ProviderOrganizationId = "org-legacy-2";

        var created = await _accounts.GetOrCreateAndReconcileAsync(first, CancellationToken.None);

        created.ProviderId.Should().Be("provider-row-original");

        var second = NewAccount(tenantId, "org-legacy-2");
        second.ProviderId = "provider-row-different";
        second.ProviderOrganizationId = "org-legacy-2-different";

        var reconciled = await _accounts.GetOrCreateAndReconcileAsync(
            second,
            CancellationToken.None);

        reconciled.ItemId.Should().Be(created.ItemId);
        reconciled.ProviderId.Should().Be(
            "provider-row-original",
            "a frozen provider identity must survive unchanged even when a later reconcile call " +
            "supplies a different one");
        reconciled.ProviderOrganizationId.Should().Be("org-legacy-2");

        var reloaded = await _accounts.GetAsync(tenantId, created.ItemId, CancellationToken.None);

        reloaded!.ProviderId.Should().Be("provider-row-original");
        reloaded.ProviderOrganizationId.Should().Be("org-legacy-2");
    }

    /// <summary>
    /// PR #393 review, Finding 2: a losing writer in the backfill race must never return the
    /// stale, pre-update in-memory value it read before attempting the conditional update. Two
    /// concurrent reconcile calls for the same legacy (null-<c>ProviderId</c>) billing account
    /// race to backfill it; exactly one call's conditional update actually applies, but both
    /// calls must agree on the same non-null winning value -- a losing caller returning
    /// <c>ProviderId == null</c> would silently skip the fail-closed <c>ExpectedProviderId</c>
    /// check even though the database already has a frozen identity.
    /// </summary>
    [Fact]
    public async Task Concurrent_backfills_of_the_same_legacy_account_agree_on_one_non_null_provider_id()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var legacy = NewAccount(tenantId, "org-legacy-race");
        var created = await _accounts.GetOrCreateAndReconcileAsync(legacy, CancellationToken.None);
        created.ProviderId.Should().BeNull();

        var first = NewAccount(tenantId, "org-legacy-race");
        first.ProviderId = "provider-row-first";
        first.ProviderOrganizationId = "org-legacy-race";

        var second = NewAccount(tenantId, "org-legacy-race");
        second.ProviderId = "provider-row-second";
        second.ProviderOrganizationId = "org-legacy-race";

        var results = await Task.WhenAll(
            _accounts.GetOrCreateAndReconcileAsync(first, CancellationToken.None),
            _accounts.GetOrCreateAndReconcileAsync(second, CancellationToken.None));

        results[0].ProviderId.Should().NotBeNull();
        results[1].ProviderId.Should().NotBeNull();
        results[0].ProviderId.Should().Be(
            results[1].ProviderId,
            "both concurrent callers must agree on whichever value actually won the race, " +
            "neither may report a null identity the database no longer has");

        var reloaded = await _accounts.GetAsync(tenantId, created.ItemId, CancellationToken.None);
        reloaded!.ProviderId.Should().Be(results[0].ProviderId);
    }

    [Fact]
    public async Task An_event_is_appended_once_per_deduplication_key()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = NewSubscription(tenantId, "org-11", SubscriptionStatus.Active);

        await _subscriptions.TryCreateAsync(
            subscription,
            CancellationToken.None);

        var outboxEvent = new SubscriptionOutboxEvent
        {
            EventType = "SubscriptionActivated",
            DeduplicationKey = $"{subscription.ItemId}:SubscriptionActivated",
            Payload = "{}",
            CorrelationId = "corr-1"
        };

        (await _subscriptions.TryAppendEventAsync(
            tenantId, subscription.ItemId, outboxEvent,
            CancellationToken.None)).Should().BeTrue();

        (await _subscriptions.TryAppendEventAsync(
                tenantId, subscription.ItemId, outboxEvent,
                CancellationToken.None))
            .Should().BeFalse();
    }

    private static SubscriptionDetail NewSubscription(
        string tenantId,
        string organizationId,
        SubscriptionStatus status)
    {
        var id = Guid.NewGuid().ToString();

        return new SubscriptionDetail
        {
            ItemId = id,
            TenantId = tenantId,
            OrganizationId = organizationId,
            BillingAccountId = "acct-1",
            Status = status,
            CurrencyCode = "CHF",
            OrderId = $"sub:{id}"
        };
    }

    private static SubscriptionUsageRecord NewUsageRecord(
        string tenantId,
        string subscriptionId,
        string idempotencyKey) => new()
    {
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = subscriptionId,
        MeterKey = "screening",
        PeriodKey = "M20260801T000000Z",
        Delta = 1,
        IdempotencyKey = idempotencyKey,
        OccurredAtUtc = DateTime.UtcNow
    };

    private static SubscriptionUsageCounter NewCounter(
        string tenantId,
        string subscriptionId,
        string meterKey,
        string periodKey) => new()
    {
        ItemId = SubscriptionUsageCounter.CreateId(subscriptionId, meterKey, periodKey),
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = subscriptionId,
        MeterKey = meterKey,
        PeriodKey = periodKey,
        LimitSnapshot = 500,
        PeriodStartUtc = DateTime.UtcNow.AddDays(-1),
        PeriodEndUtc = DateTime.UtcNow.AddDays(29),
        ExpiresAtUtc = DateTime.UtcNow.AddDays(400)
    };

    private static SubscriptionPaymentLink NewLink(
        string tenantId,
        string subscriptionId,
        string paymentDetailId) => new()
    {
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = subscriptionId,
        PaymentDetailId = paymentDetailId,
        OrderId = $"sub:{subscriptionId}"
    };

    private static BillingAccount NewAccount(string tenantId, string organizationId) => new()
    {
        TenantId = tenantId,
        OrganizationId = organizationId,
        ProviderName = "STRIPE"
    };
}
