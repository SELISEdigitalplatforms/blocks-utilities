using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The guarantees that make "every financial event reaches the ledger" true rather than hoped for.
/// </summary>
/// <remarks>
/// The ledger's own indexes prove a document cannot be issued twice. They say nothing about whether it
/// is issued at all, and that is a different set of properties, resting on different MongoDB
/// behaviours: an obligation appended in the same update as the transition that caused it, a sweep
/// query that has no time window to fall outside of, and a high-water mark that only moves forward
/// however many workers are pushing it.
/// <para>
/// None of these can be shown against an in-memory stand-in, because every one of them is about what
/// the database does when two writers arrive at once or when a process dies between two writes.
/// </para>
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriptionDocumentSourceIntegrationTests
{
    private readonly SubscriptionRepository _subscriptions;
    private readonly SubscriptionDocumentCursorRepository _cursors;
    private readonly SubscriptionMerchantProfileRepository _merchants;

    public SubscriptionDocumentSourceIntegrationTests(MongoIntegrationFixture fixture)
    {
        _subscriptions = new SubscriptionRepository(fixture.DbContextProvider);
        _cursors = new SubscriptionDocumentCursorRepository(fixture.DbContextProvider);
        _merchants = new SubscriptionMerchantProfileRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task A_change_that_banks_credit_carries_its_credit_note_in_the_same_write()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = await StoredAsync(tenantId);

        var applied = await _subscriptions.TryApplyQuantityChangeAsync(
            tenantId,
            subscription.ItemId,
            subscription.Version,
            [Seat(2)],
            newCreditBalanceMinor: 4_250,
            quantityChangePaymentDetailId: null,
            NewEvent("quantity-changed"),
            CancellationToken.None,
            Source("downgrade:sub:v1", FinancialDocumentType.CreditNote, 4_250));

        applied.Should().BeTrue();

        var reloaded = await _subscriptions.GetByIdAsync(
            tenantId,
            subscription.ItemId,
            CancellationToken.None);

        // The credit and the obligation to document it, committed together. Recording the obligation
        // in a second write would lose it to any crash in between, and there is nothing else to
        // reconstruct it from: no payment was taken, and the balance cannot say which change moved it.
        reloaded!.CreditBalanceMinor.Should().Be(4_250);
        reloaded.PendingDocumentSources.Should().ContainSingle()
            .Which.CreditedMinor.Should().Be(4_250);
    }

    [Fact]
    public async Task A_change_that_loses_the_version_race_records_no_obligation_either()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = await StoredAsync(tenantId);

        var applied = await _subscriptions.TryApplyQuantityChangeAsync(
            tenantId,
            subscription.ItemId,
            // A version nobody is at, standing in for a concurrent change having moved it.
            subscription.Version + 7,
            [Seat(2)],
            newCreditBalanceMinor: 4_250,
            quantityChangePaymentDetailId: null,
            NewEvent("quantity-changed"),
            CancellationToken.None,
            Source("downgrade:sub:v1", FinancialDocumentType.CreditNote, 4_250));

        applied.Should().BeFalse();

        var reloaded = await _subscriptions.GetByIdAsync(
            tenantId,
            subscription.ItemId,
            CancellationToken.None);

        // The other half of atomicity, and the half that is easy to get wrong: a change that did not
        // happen must not leave a credit note owing for it. A subscriber would be sent a document for
        // value they were never given.
        reloaded!.CreditBalanceMinor.Should().Be(0);
        reloaded.PendingDocumentSources.Should().BeEmpty();
    }

    [Fact]
    public async Task One_event_appends_one_obligation_however_many_times_it_is_announced()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = await StoredAsync(tenantId);

        var source = Source("payment:pay-1", FinancialDocumentType.Invoice, 0);

        var first = await _subscriptions.TryAppendDocumentSourceAsync(
            tenantId, subscription.ItemId, source, CancellationToken.None);
        var second = await _subscriptions.TryAppendDocumentSourceAsync(
            tenantId, subscription.ItemId, source, CancellationToken.None);

        first.Should().BeTrue();

        // A retried money path announces the same event twice. Reported as false and appended once,
        // which is success as the caller means it — two obligations would mean two attempts at a
        // document that the ledger would then refuse, wasting an invoice number each time.
        second.Should().BeFalse();

        var reloaded = await _subscriptions.GetByIdAsync(
            tenantId, subscription.ItemId, CancellationToken.None);

        reloaded!.PendingDocumentSources.Should().ContainSingle();
    }

    [Fact]
    public async Task An_obligation_of_any_age_is_found_because_the_sweep_query_has_no_window()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var recent = await StoredAsync(tenantId);
        var ancient = await StoredAsync(tenantId, lastUpdatedUtc: DateTime.UtcNow.AddYears(-3));
        await StoredAsync(tenantId);

        await _subscriptions.TryAppendDocumentSourceAsync(
            tenantId, recent.ItemId, Source("payment:pay-1", FinancialDocumentType.Invoice, 0),
            CancellationToken.None);
        await _subscriptions.TryAppendDocumentSourceAsync(
            tenantId, ancient.ItemId, Source("payment:pay-2", FinancialDocumentType.Invoice, 0),
            CancellationToken.None);

        var owing = await _subscriptions.ListWithPendingDocumentSourcesAsync(
            tenantId, maximumAttempts: 8, limit: 25, CancellationToken.None);

        // Both, and only these two. A fixed lookback would have found the recent one and left the
        // three-year-old document unissued for good, with nothing recording that it happened.
        owing.Select(subscription => subscription.ItemId)
            .Should().BeEquivalentTo([ancient.ItemId, recent.ItemId]);

        // Oldest obligation first, so a backlog drains in the order the events happened rather than
        // letting a busy subscription's newest jump one that has been waiting since the outage.
        owing[0].ItemId.Should().Be(ancient.ItemId);
    }

    [Fact]
    public async Task An_obligation_that_can_never_be_composed_stops_starving_the_others()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = await StoredAsync(tenantId);

        await _subscriptions.TryAppendDocumentSourceAsync(
            tenantId, subscription.ItemId,
            Source("payment:pay-1", FinancialDocumentType.Invoice, 0),
            CancellationToken.None);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            (await _subscriptions.RecordDocumentSourceFailureAsync(
                tenantId,
                subscription.ItemId,
                "payment:pay-1",
                "document_compose_failed",
                CancellationToken.None))
                .Should().BeTrue();
        }

        var reloaded = await _subscriptions.GetByIdAsync(
            tenantId, subscription.ItemId, CancellationToken.None);

        reloaded!.PendingDocumentSources.Single().AttemptCount.Should().Be(3);
        reloaded.PendingDocumentSources.Single().LastError.Should().Be("document_compose_failed");

        // Left out once its attempts are spent, so one document nothing can compose cannot occupy
        // every batch forever while other subscriptions wait behind it. The obligation stays on the
        // record, which is what an operator needs to see.
        (await _subscriptions.ListWithPendingDocumentSourcesAsync(
            tenantId, maximumAttempts: 3, limit: 25, CancellationToken.None))
            .Should().BeEmpty();

        (await _subscriptions.ListWithPendingDocumentSourcesAsync(
            tenantId, maximumAttempts: 4, limit: 25, CancellationToken.None))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task An_obligation_is_cleared_only_by_its_own_key()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = await StoredAsync(tenantId);

        await _subscriptions.TryAppendDocumentSourceAsync(
            tenantId, subscription.ItemId,
            Source("payment:pay-1", FinancialDocumentType.Invoice, 0),
            CancellationToken.None);
        await _subscriptions.TryAppendDocumentSourceAsync(
            tenantId, subscription.ItemId,
            Source("trial:sub-1:2026", FinancialDocumentType.TrialInvoice, 0),
            CancellationToken.None);

        (await _subscriptions.TryConsumeDocumentSourceAsync(
            tenantId, subscription.ItemId, "payment:pay-1", CancellationToken.None))
            .Should().BeTrue();

        var reloaded = await _subscriptions.GetByIdAsync(
            tenantId, subscription.ItemId, CancellationToken.None);

        // One document written must not discharge another's obligation. A subscription owing a trial
        // invoice and a settlement invoice at once is ordinary — a trial that took a card does both.
        reloaded!.PendingDocumentSources.Should().ContainSingle()
            .Which.SourceKey.Should().Be("trial:sub-1:2026");
    }

    [Fact]
    public async Task A_trial_is_findable_by_when_it_started_however_long_ago_that_was()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var old = await StoredAsync(tenantId, trialStartUtc: new DateTime(
            2024, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var recent = await StoredAsync(tenantId, trialStartUtc: new DateTime(
            2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        await StoredAsync(tenantId);

        var walked = await _subscriptions.ListTrialsStartedSinceAsync(
            tenantId, DateTime.MinValue.ToUniversalTime(), null, 25, CancellationToken.None);

        // The backstop for the one obligation that leaves no payment behind and can predate the
        // mechanism that records it. Oldest first, so the mark advances through history in order.
        walked.Select(subscription => subscription.ItemId).Should().Equal([old.ItemId, recent.ItemId]);

        // And from a mark part-way through, only what comes after it.
        (await _subscriptions.ListTrialsStartedSinceAsync(
            tenantId, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, 25,
            CancellationToken.None))
            .Should().ContainSingle().Which.ItemId.Should().Be(recent.ItemId);
    }

    [Fact]
    public async Task A_sweep_mark_only_ever_moves_forward()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        const string cursor = "document-settled-charges";

        (await _cursors.GetAsync(tenantId, cursor, CancellationToken.None)).Should().BeNull();

        var later = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);
        var earlier = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await _cursors.SetAsync(
            tenantId, cursor, new FinancialDocumentSweepMark(later, "b"), CancellationToken.None);
        await _cursors.SetAsync(
            tenantId, cursor, new FinancialDocumentSweepMark(earlier, "a"), CancellationToken.None);

        // Two workers sweeping one tenant converge on the furthest either reached rather than taking
        // turns dragging the mark backwards, which would have them re-scanning the same stretch of
        // history forever without ever finishing it.
        (await _cursors.GetAsync(tenantId, cursor, CancellationToken.None))!
            .Value.ReadUpToUtc.Should().Be(later);
    }

    [Fact]
    public async Task A_mark_advances_within_an_instant_because_that_is_how_a_page_makes_progress()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        const string cursor = "document-settled-charges";

        var instant = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

        await _cursors.SetAsync(
            tenantId, cursor, new FinancialDocumentSweepMark(instant, "pay-24"),
            CancellationToken.None);
        await _cursors.SetAsync(
            tenantId, cursor, new FinancialDocumentSweepMark(instant, "pay-29"),
            CancellationToken.None);

        // The case a mark of one instant cannot express. Thirty charges settling in the same instant
        // are read a page at a time, so the mark has to move *within* that instant or the pass after
        // the first re-reads the same page forever. Comparing on the instant alone — which $max does —
        // would refuse this write and reinstate the livelock.
        var stored = await _cursors.GetAsync(tenantId, cursor, CancellationToken.None);

        stored!.Value.ReadUpToUtc.Should().Be(instant);
        stored.Value.AfterId.Should().Be("pay-29");
    }

    [Fact]
    public async Task A_mark_never_moves_backwards_within_an_instant_either()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        const string cursor = "document-trials";

        var instant = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

        await _cursors.SetAsync(
            tenantId, cursor, new FinancialDocumentSweepMark(instant, "sub-90"),
            CancellationToken.None);
        await _cursors.SetAsync(
            tenantId, cursor, new FinancialDocumentSweepMark(instant, "sub-10"),
            CancellationToken.None);

        // Monotonic over the whole mark, not just its instant. A worker that got less far must not be
        // able to pull the mark back to its own position and have the pair re-read.
        (await _cursors.GetAsync(tenantId, cursor, CancellationToken.None))!
            .Value.AfterId.Should().Be("sub-90");
    }

    [Fact]
    public async Task Writing_a_mark_that_is_already_behind_does_not_insert_a_second_one()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        const string cursor = "document-refunds";

        var later = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

        await _cursors.SetAsync(
            tenantId, cursor, new FinancialDocumentSweepMark(later, "pay-9"),
            CancellationToken.None);

        // The pitfall this is written two operations to avoid: an upsert filtered on "the stored mark
        // is older" matches nothing when it is newer, and then tries to insert a second document under
        // the same _id. That throws a duplicate key, which would surface as a failing sweep rather than
        // as the no-op it actually is.
        var act = async () => await _cursors.SetAsync(
            tenantId,
            cursor,
            new FinancialDocumentSweepMark(later.AddDays(-1), "pay-1"),
            CancellationToken.None);

        await act.Should().NotThrowAsync();

        (await _cursors.GetAsync(tenantId, cursor, CancellationToken.None))!
            .Value.ReadUpToUtc.Should().Be(later);
    }

    [Fact]
    public async Task A_page_resumes_after_the_last_trial_it_accounted_for()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        // Three trials starting in the same instant, which is what a migration or a promotion produces
        // and what an instant-only mark cannot page through.
        var instant = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var trials = new List<SubscriptionDetail>();

        for (var index = 0; index < 3; index++)
        {
            trials.Add(await StoredAsync(tenantId, trialStartUtc: instant));
        }

        var ordered = trials.Select(trial => trial.ItemId).OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var page = await _subscriptions.ListTrialsStartedSinceAsync(
            tenantId, instant, null, 2, CancellationToken.None);

        page.Select(trial => trial.ItemId).Should().Equal(ordered.Take(2));

        // Resumed after the second, which reaches the third. With no identifier in the mark this second
        // read would have returned the same first two, forever.
        var next = await _subscriptions.ListTrialsStartedSinceAsync(
            tenantId, instant, ordered[1], 2, CancellationToken.None);

        next.Should().ContainSingle().Which.ItemId.Should().Be(ordered[2]);

        // And nothing after the last, which is how a pass knows it has caught up.
        (await _subscriptions.ListTrialsStartedSinceAsync(
            tenantId, instant, ordered[2], 2, CancellationToken.None))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_workers_cannot_drag_a_mark_backwards_between_them()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        const string cursor = "document-refunds";

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Forty writes in a random-ish order, all at once. The final value has to be the maximum
        // regardless of which lands last, which is a property of the update operator rather than of
        // anything this code can arrange.
        await Task.WhenAll(Enumerable.Range(0, 40).Select(index => _cursors.SetAsync(
            tenantId,
            cursor,
            new FinancialDocumentSweepMark(
                start.AddMinutes((index * 17) % 40),
                $"pay-{(index * 17) % 40:D2}"),
            CancellationToken.None)));

        (await _cursors.GetAsync(tenantId, cursor, CancellationToken.None))!
            .Value.ReadUpToUtc.Should().Be(start.AddMinutes(39));
    }

    [Fact]
    public async Task A_tenant_can_only_have_one_seller()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var first = await _merchants.UpsertAsync(
            new SubscriptionMerchantProfile
            {
                TenantId = tenantId,
                LegalName = "Northwind Software GmbH",
                TaxRegistrationId = "DE811234567"
            },
            CancellationToken.None);

        var second = await _merchants.UpsertAsync(
            new SubscriptionMerchantProfile
            {
                TenantId = tenantId,
                LegalName = "Northwind Software AG"
            },
            CancellationToken.None);

        // The same document both times, edited rather than duplicated. Two merchant profiles would be
        // two answers to who issued an invoice, and the upsert would silently pick one of them.
        second.ItemId.Should().Be(first.ItemId);
        second.LegalName.Should().Be("Northwind Software AG");
        second.Version.Should().Be(2);

        // Cleared rather than carried over: the new seller has no tax registration, and printing the
        // previous one would attribute this tenant's invoices to a registration it does not hold.
        second.TaxRegistrationId.Should().BeNull();

        (await _merchants.GetAsync(tenantId, CancellationToken.None))!
            .LegalName.Should().Be("Northwind Software AG");
    }

    [Fact]
    public async Task Two_tenants_issue_under_two_different_sellers()
    {
        var first = MongoIntegrationFixture.NewTenantId();
        var second = MongoIntegrationFixture.NewTenantId();

        await _merchants.UpsertAsync(
            new SubscriptionMerchantProfile { TenantId = first, LegalName = "Northwind GmbH" },
            CancellationToken.None);
        await _merchants.UpsertAsync(
            new SubscriptionMerchantProfile { TenantId = second, LegalName = "Contoso SA" },
            CancellationToken.None);

        // The whole point of storing this per tenant. A single configured identity had every tenant
        // in the deployment issuing documents under one company's legal name.
        (await _merchants.GetAsync(first, CancellationToken.None))!
            .LegalName.Should().Be("Northwind GmbH");
        (await _merchants.GetAsync(second, CancellationToken.None))!
            .LegalName.Should().Be("Contoso SA");
    }

    private async Task<SubscriptionDetail> StoredAsync(
        string tenantId,
        DateTime? lastUpdatedUtc = null,
        DateTime? trialStartUtc = null)
    {
        var subscription = new SubscriptionDetail
        {
            TenantId = tenantId,
            OrganizationId = $"org-{Guid.NewGuid():N}",
            BillingAccountId = "acct-1",
            Status = SubscriptionStatus.Active,
            CurrencyCode = "CHF",
            Plan = new PlanSnapshot { Code = "pro", DisplayName = "Pro" },
            Price = new PriceSnapshot
            {
                PriceId = "price-1",
                CurrencyCode = "CHF",
                UnitAmountMinor = 100_000,
                Interval = BillingInterval.Month,
                IntervalCount = 1
            },
            QuantityItems = [Seat(1)],
            LastUpdatedDateUtc = lastUpdatedUtc ?? DateTime.UtcNow,
            Trial = trialStartUtc is { } start
                ? new TrialTerms { StartsAtUtc = start, EndsAtUtc = start.AddDays(14) }
                : null
        };

        (await _subscriptions.TryCreateAsync(subscription, CancellationToken.None))
            .Should().BeTrue();

        return subscription;
    }

    private static SubscriptionQuantityItem Seat(long quantity) => new()
    {
        ItemKey = "seats",
        UnitLabel = "Seats",
        Quantity = quantity,
        UnitAmountMinor = 10_000
    };

    private static SubscriptionDocumentSource Source(
        string sourceKey,
        FinancialDocumentType documentType,
        long creditedMinor) =>
        new()
        {
            SourceKey = sourceKey,
            DocumentType = documentType,
            ChargeKind = SubscriptionChargeKind.PlanChange,
            CurrencyCode = "CHF",
            CreditedMinor = creditedMinor,
            Subject = new FinancialDocumentSubject { PlanCode = "pro", PlanName = "Pro" },
            OccurredAtUtc = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc),
            CorrelationId = "corr-1"
        };

    private static SubscriptionOutboxEvent NewEvent(string eventType) => new()
    {
        EventType = eventType,
        DeduplicationKey = $"{eventType}:{Guid.NewGuid():N}",
        Payload = "{}",
        CorrelationId = "corr-1"
    };
}
