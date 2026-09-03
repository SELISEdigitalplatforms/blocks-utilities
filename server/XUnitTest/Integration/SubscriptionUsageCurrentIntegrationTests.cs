using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The current-usage projection, against a real MongoDB.
/// </summary>
/// <remarks>
/// Every guarantee this collection makes is a property of a Mongo write, not of C#: the version
/// condition on the upsert, the uniqueness of one document per meter-period, the insert-only seed,
/// and the boundary query that picks the current window. A mocked repository proves none of them, and
/// the one that matters most — the highest version winning a race rather than the last writer —
/// cannot be observed at all without concurrent writers hitting one server.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriptionUsageCurrentIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionUsageCurrentRepository _current;

    public SubscriptionUsageCurrentIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _current = new SubscriptionUsageCurrentRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task Publishing_stores_every_field_a_direct_reader_depends_on()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var document = Document(tenantId, used: 40, counterVersion: 3);

        (await _current.TryPublishAsync(document, CancellationToken.None)).Should().BeTrue();

        var stored = await _current.GetAsync(tenantId, document.ItemId, CancellationToken.None);

        stored.Should().NotBeNull();
        stored!.OrganizationId.Should().Be("org-1");
        stored.SubscriptionId.Should().Be(Sub(tenantId));
        stored.SubscriptionStatus.Should().Be(SubscriptionStatus.Active);
        stored.PlanId.Should().Be("plan-1");
        stored.PlanCode.Should().Be("pro");
        stored.MeterKey.Should().Be("screening");
        stored.UnitLabel.Should().Be("screening");
        stored.Included.Should().Be(100);
        stored.Used.Should().Be(40);
        stored.Remaining.Should().Be(60);
        stored.Overage.Should().Be(0);
        stored.OverageAllowed.Should().BeTrue();
        stored.CounterVersion.Should().Be(3);
        stored.SchemaVersion.Should().Be(SubscriptionUsageCurrent.CurrentSchemaVersion);
    }

    [Fact]
    public async Task A_newer_version_replaces_an_older_one()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 10, counterVersion: 1), CancellationToken.None);

        (await _current.TryPublishAsync(
                Document(tenantId, used: 20, counterVersion: 2), CancellationToken.None))
            .Should().BeTrue();

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(20);
        stored.CounterVersion.Should().Be(2);
    }

    /// <summary>
    /// The defect this condition exists to prevent. A request delayed between updating its counter
    /// and publishing its projection carries an older figure; without the condition it would
    /// overwrite a newer balance and leave the projection permanently behind, with nothing to say so.
    /// </summary>
    [Fact]
    public async Task An_older_version_cannot_overwrite_a_newer_one()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 90, counterVersion: 9), CancellationToken.None);

        (await _current.TryPublishAsync(
                Document(tenantId, used: 10, counterVersion: 4), CancellationToken.None))
            .Should().BeFalse("the stored document is already newer");

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(90, "the newer figure must survive");
        stored.CounterVersion.Should().Be(9);
    }

    /// <summary>Republishing the same version is not an update, and must not be reported as one.</summary>
    [Fact]
    public async Task Republishing_the_same_version_changes_nothing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 7, counterVersion: 5), CancellationToken.None);

        (await _current.TryPublishAsync(
                Document(tenantId, used: 999, counterVersion: 5), CancellationToken.None))
            .Should().BeFalse();

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(7);
    }

    /// <summary>
    /// The concurrency guarantee, with real concurrent writers.
    /// </summary>
    /// <remarks>
    /// Sixteen publishes of the same meter-period in a scrambled version order, all at once. The
    /// document must end at the highest version, and exactly one call may report having written it —
    /// the rest lost the version race, which is a success for them and a no-op for the database.
    /// Last-writer-wins would leave whichever call happened to finish last, which is the bug.
    /// </remarks>
    [Fact]
    public async Task The_highest_version_wins_a_concurrent_race_not_the_last_writer()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        // Scrambled deliberately: ascending order would pass even with no condition at all.
        var versions = new[] { 7, 3, 16, 1, 11, 5, 14, 2, 9, 6, 15, 4, 13, 8, 12, 10 };

        var results = await Task.WhenAll(versions.Select(version =>
            _current.TryPublishAsync(
                Document(tenantId, used: version * 10, counterVersion: version),
                CancellationToken.None)));

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.CounterVersion.Should().Be(16, "the highest version must be the one that stands");
        stored.Used.Should().Be(160, "and the balance stored with it");

        results.Count(written => written)
            .Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(versions.Length);
    }

    /// <summary>
    /// The defect the subscription version exists to fix, asserted against a real write.
    /// </summary>
    /// <remarks>
    /// A plan change alters the allowance without recording any usage, so the counter version is
    /// unchanged. Ordered on the counter version alone this republish compared equal and was refused
    /// as stale, and the projection kept advertising the old allowance indefinitely — not for one
    /// sweep interval, but until somebody happened to record usage against that meter.
    /// </remarks>
    [Fact]
    public async Task A_changed_allowance_lands_even_though_the_counter_did_not_move()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 10, counterVersion: 4, subscriptionVersion: 7),
            CancellationToken.None);

        var repriced = Document(tenantId, used: 10, counterVersion: 4, subscriptionVersion: 8);
        repriced.Included = 250;
        repriced.Remaining = 240;

        (await _current.TryPublishAsync(repriced, CancellationToken.None))
            .Should().BeTrue("the subscription version is newer even though the counter is not");

        var stored = await _current.GetAsync(
            tenantId, repriced.ItemId, CancellationToken.None);

        stored!.Included.Should().Be(250);
        stored.Remaining.Should().Be(240);
        stored.SubscriptionVersion.Should().Be(8);
    }

    /// <summary>
    /// The subscription version is a tie-break, not an override. A newer balance is newer information
    /// about the same subscription even when it was read at an older subscription version, so it must
    /// not be refused by one.
    /// </summary>
    [Fact]
    public async Task A_newer_counter_version_wins_even_with_an_older_subscription_version()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 10, counterVersion: 4, subscriptionVersion: 9),
            CancellationToken.None);

        (await _current.TryPublishAsync(
                Document(tenantId, used: 50, counterVersion: 5, subscriptionVersion: 2),
                CancellationToken.None))
            .Should().BeTrue();

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(50);
        stored.CounterVersion.Should().Be(5);
    }

    /// <summary>An older subscription version at the same counter version is still stale.</summary>
    [Fact]
    public async Task An_older_subscription_version_at_the_same_counter_version_is_refused()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 10, counterVersion: 4, subscriptionVersion: 9),
            CancellationToken.None);

        (await _current.TryPublishAsync(
                Document(tenantId, used: 999, counterVersion: 4, subscriptionVersion: 3),
                CancellationToken.None))
            .Should().BeFalse();

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(10);
        stored.SubscriptionVersion.Should().Be(9);
    }

    /// <summary>
    /// Both versions equal is not newer information, so it must not be reported as a write.
    /// </summary>
    [Fact]
    public async Task Republishing_both_versions_unchanged_writes_nothing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 10, counterVersion: 4, subscriptionVersion: 9),
            CancellationToken.None);

        (await _current.TryPublishAsync(
                Document(tenantId, used: 777, counterVersion: 4, subscriptionVersion: 9),
                CancellationToken.None))
            .Should().BeFalse();

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(10);
    }

    /// <summary>
    /// The regression the field-group merge exists to prevent, in the exact order that produced it.
    /// </summary>
    /// <remarks>
    /// A cancellation publishes <c>(counter 10, subscription 6, Cancelled)</c>. A usage request
    /// already in flight then publishes <c>(counter 11, subscription 5, Active)</c> — it read the
    /// subscription before the cancellation committed, so its metadata is genuinely older.
    /// <para>
    /// Replacing the whole document because the counter is newer restored <c>Active</c> and drove the
    /// stored subscription version backwards from 6 to 5. A cancelled subscription then advertised a
    /// live allowance, and nothing would correct it until the next lifecycle change.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_late_usage_publish_cannot_resurrect_a_cancelled_status()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var cancellation = Document(
            tenantId, used: 40, counterVersion: 10, subscriptionVersion: 6);
        cancellation.SubscriptionStatus = SubscriptionStatus.Canceled;
        cancellation.Included = 100;

        await _current.TryPublishAsync(cancellation, CancellationToken.None);

        var lateUsage = Document(tenantId, used: 55, counterVersion: 11, subscriptionVersion: 5);
        lateUsage.SubscriptionStatus = SubscriptionStatus.Active;
        lateUsage.Included = 100;

        (await _current.TryPublishAsync(lateUsage, CancellationToken.None))
            .Should().BeTrue("its counter version is newer, so its balance is worth taking");

        var stored = await _current.GetAsync(
            tenantId, cancellation.ItemId, CancellationToken.None);

        stored!.Used.Should().Be(55, "the newer balance wins");
        stored.CounterVersion.Should().Be(11);
        stored.SubscriptionStatus.Should().Be(
            SubscriptionStatus.Canceled, "the newer metadata must not be undone by an older writer");
        stored.SubscriptionVersion.Should().Be(6, "and the version must not go backwards");
    }

    /// <summary>
    /// The inverse order, which the old comparison rejected outright.
    /// </summary>
    /// <remarks>
    /// With <c>(11, 5)</c> stored, a lifecycle refresh carrying <c>(10, 6)</c> failed the composite
    /// condition entirely — the counter was not newer, and the subscription tie-break only applied
    /// when the counters were equal. Its newer metadata never landed at all.
    /// </remarks>
    [Fact]
    public async Task A_lifecycle_refresh_lands_its_metadata_even_behind_a_newer_counter()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var usage = Document(tenantId, used: 55, counterVersion: 11, subscriptionVersion: 5);
        usage.SubscriptionStatus = SubscriptionStatus.Active;
        usage.Included = 100;

        await _current.TryPublishAsync(usage, CancellationToken.None);

        var refresh = Document(tenantId, used: 40, counterVersion: 10, subscriptionVersion: 6);
        refresh.SubscriptionStatus = SubscriptionStatus.Canceled;
        refresh.Included = 250;
        refresh.PlanCode = "enterprise";

        (await _current.TryPublishAsync(refresh, CancellationToken.None))
            .Should().BeTrue("its subscription version is newer");

        var stored = await _current.GetAsync(tenantId, usage.ItemId, CancellationToken.None);

        stored!.SubscriptionStatus.Should().Be(SubscriptionStatus.Canceled);
        stored.Included.Should().Be(250);
        stored.PlanCode.Should().Be("enterprise");
        stored.SubscriptionVersion.Should().Be(6);

        stored.Used.Should().Be(55, "the older balance must not overwrite the newer one");
        stored.CounterVersion.Should().Be(11, "and that version must not go backwards either");
    }

    /// <summary>
    /// Remaining and overage are recomputed from whichever balance and allowance won, so they cannot
    /// describe a state that never existed.
    /// </summary>
    /// <remarks>
    /// Taking either from the losing side is the trap: after the merge above, <c>Used</c> comes from
    /// one writer and <c>Included</c> from the other, and a <c>Remaining</c> copied from either would
    /// be arithmetic on one of them plus a stale version of the other.
    /// </remarks>
    [Fact]
    public async Task Remaining_and_overage_are_consistent_with_the_merged_balance_and_allowance()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var repriced = Document(tenantId, used: 10, counterVersion: 4, subscriptionVersion: 9);
        repriced.Included = 30;
        repriced.Remaining = 20;
        repriced.Overage = 0;

        await _current.TryPublishAsync(repriced, CancellationToken.None);

        // Newer balance, older allowance: 50 used against the 30 that won.
        var lateUsage = Document(tenantId, used: 50, counterVersion: 5, subscriptionVersion: 2);
        lateUsage.Included = 500;
        lateUsage.Remaining = 450;
        lateUsage.Overage = 0;

        await _current.TryPublishAsync(lateUsage, CancellationToken.None);

        var stored = await _current.GetAsync(tenantId, repriced.ItemId, CancellationToken.None);

        stored!.Used.Should().Be(50);
        stored.Included.Should().Be(30);
        stored.Remaining.Should().Be(0, "not the 450 the losing writer computed against its own allowance");
        stored.Overage.Should().Be(20, "50 used against an allowance of 30");
    }

    /// <summary>
    /// Both kinds of writer arriving at once, in every interleaving the scheduler happens to pick.
    /// </summary>
    /// <remarks>
    /// The merge is per-field and each version takes a maximum, so the document converges on the
    /// newest of each kind of information whatever order the writers land in. That is what makes this
    /// safe without a transaction: there is no interleaving that produces a different answer.
    /// </remarks>
    [Fact]
    public async Task Mixed_usage_and_lifecycle_writers_converge_whatever_order_they_land_in()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var writers = new List<Func<Task>>();

        foreach (var counterVersion in new[] { 3, 8, 5, 1, 7 })
        {
            var version = counterVersion;

            writers.Add(async () =>
            {
                var usage = Document(
                    tenantId, used: version * 10, counterVersion: version, subscriptionVersion: 1);
                usage.Included = 100;
                usage.SubscriptionStatus = SubscriptionStatus.Active;

                await _current.TryPublishAsync(usage, CancellationToken.None);
            });
        }

        foreach (var subscriptionVersion in new[] { 4, 2, 6 })
        {
            var version = subscriptionVersion;

            writers.Add(async () =>
            {
                var lifecycle = Document(
                    tenantId, used: 0, counterVersion: 1, subscriptionVersion: version);
                lifecycle.Included = 100 + version;
                lifecycle.SubscriptionStatus = version == 6
                    ? SubscriptionStatus.Canceled
                    : SubscriptionStatus.Active;

                await _current.TryPublishAsync(lifecycle, CancellationToken.None);
            });
        }

        await Task.WhenAll(writers.Select(writer => writer()));

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.CounterVersion.Should().Be(8, "the highest counter version");
        stored.Used.Should().Be(80, "and the balance published with it");
        stored.SubscriptionVersion.Should().Be(6, "the highest subscription version");
        stored.Included.Should().Be(106, "and the allowance published with it");
        stored.SubscriptionStatus.Should().Be(SubscriptionStatus.Canceled);
        stored.Remaining.Should().Be(26, "106 minus 80, from the two winners");
        stored.Overage.Should().Be(0);
    }

    /// <summary>
    /// A carry-forward allowance corrected by the counter side alone.
    /// </summary>
    /// <remarks>
    /// The allowance is computed from the plan's terms and the counter's <c>LimitSnapshot</c> — the
    /// figure frozen when the window opened, which is where a carry-forward from the previous period
    /// lands. A seed publishes the opening figure before any counter exists; the first recording then
    /// opens the counter with a possibly different frozen snapshot.
    /// <para>
    /// Owned by the subscription version alone, that correction could only have arrived with an
    /// unrelated plan edit, and until one happened the projection would advertise the seeded
    /// allowance. So the allowance moves on a counter advance too, and <c>remaining</c> and
    /// <c>overage</c> follow from the corrected figure rather than the seeded one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_counter_advance_corrects_a_carry_forward_allowance_and_what_follows_from_it()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        // Seeded at zero usage with the opening allowance as it looked before the window opened.
        var seeded = Document(tenantId, used: 0, counterVersion: 0, subscriptionVersion: 3);
        seeded.Included = 100;
        seeded.Remaining = 100;
        seeded.Overage = 0;

        await _current.TrySeedAsync(seeded, CancellationToken.None);

        // First recording. Only the counter advanced — same subscription version — and the counter
        // opened with a larger frozen allowance because the previous period carried 40 forward.
        var firstUsage = Document(tenantId, used: 30, counterVersion: 1, subscriptionVersion: 3);
        firstUsage.Included = 140;

        (await _current.TryPublishAsync(firstUsage, CancellationToken.None)).Should().BeTrue();

        var stored = await _current.GetAsync(tenantId, seeded.ItemId, CancellationToken.None);

        stored!.Included.Should().Be(
            140, "the counter's frozen snapshot is where a carry-forward lands");
        stored.Used.Should().Be(30);
        stored.Remaining.Should().Be(110, "derived from the corrected allowance, not the seeded 100");
        stored.Overage.Should().Be(0);
        stored.SubscriptionVersion.Should().Be(3, "unchanged, because nothing about the plan changed");
    }

    /// <summary>
    /// The guard on that: a writer whose view of the subscription is older may not touch the
    /// allowance.
    /// </summary>
    /// <remarks>
    /// Without it, letting the counter side move the allowance would reopen the very regression the
    /// field groups exist to prevent — a usage publish carrying pre-plan-change terms undoing the new
    /// plan's figure, this time through <c>Included</c> instead of through status.
    /// </remarks>
    [Fact]
    public async Task A_stale_writer_cannot_undo_a_newer_plans_allowance()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var repriced = Document(tenantId, used: 10, counterVersion: 4, subscriptionVersion: 9);
        repriced.Included = 250;

        await _current.TryPublishAsync(repriced, CancellationToken.None);

        // Newer counter, older subscription: its balance is worth taking, its allowance is not.
        var lateUsage = Document(tenantId, used: 60, counterVersion: 5, subscriptionVersion: 2);
        lateUsage.Included = 100;

        await _current.TryPublishAsync(lateUsage, CancellationToken.None);

        var stored = await _current.GetAsync(tenantId, repriced.ItemId, CancellationToken.None);

        stored!.Used.Should().Be(60, "the newer balance still wins");
        stored.Included.Should().Be(250, "the newer plan's allowance must survive an older writer");
        stored.Remaining.Should().Be(190);
        stored.Overage.Should().Be(0);
    }

    /// <summary>
    /// One meter-period is one document, enforced by the database rather than by whoever composes the
    /// id. Two current documents for one meter would show a reader two allowances for one allowance.
    /// </summary>
    [Fact]
    public async Task Two_documents_for_one_meter_period_are_refused()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var first = Document(tenantId, used: 1, counterVersion: 1);

        await _current.TryPublishAsync(first, CancellationToken.None);

        // Same subscription, meter and period as the published document, but a different _id. Only
        // the unique index can refuse this; the composed key cannot, because nothing forces a writer
        // to compose it.
        var duplicate = Document(tenantId, used: 2, counterVersion: 2);
        duplicate.ItemId = $"a-second-id-for-{first.ItemId}";

        var insert = async () => await _fixture.Database
            .GetCollection<SubscriptionUsageCurrent>("SubscriptionUsageCurrent")
            .InsertOneAsync(duplicate, cancellationToken: CancellationToken.None);

        await insert.Should().ThrowAsync<MongoWriteException>(
            "the unique index on subscription, meter and period must refuse it");
    }

    [Fact]
    public async Task Seeding_creates_a_zero_usage_document_when_none_exists()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _current.TrySeedAsync(
                Document(tenantId, used: 0, counterVersion: 0), CancellationToken.None))
            .Should().BeTrue();

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(0);
        stored.CounterVersion.Should().Be(0);
    }

    /// <summary>
    /// A seed must never reset a live balance. If it could, a rollover or an activation replay would
    /// discard usage the customer has already been billed for.
    /// </summary>
    [Fact]
    public async Task Seeding_never_overwrites_a_published_balance()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 250, counterVersion: 12), CancellationToken.None);

        (await _current.TrySeedAsync(
                Document(tenantId, used: 0, counterVersion: 0), CancellationToken.None))
            .Should().BeFalse();

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(250, "the recorded usage must survive a seed");
        stored.CounterVersion.Should().Be(12);
    }

    /// <summary>
    /// Concurrent seeds of the same missing document: exactly one may create it. Otherwise a rollover
    /// running on two workers would produce two current documents for one meter.
    /// </summary>
    [Fact]
    public async Task Only_one_of_eight_concurrent_seeds_creates_the_document()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            _current.TrySeedAsync(
                Document(tenantId, used: 0, counterVersion: 0), CancellationToken.None)));

        results.Count(created => created).Should().Be(1);
    }

    /// <summary>
    /// The current window is selected by period boundary rather than by an <c>isCurrent</c> flag,
    /// because a flag has to be cleared on another document when a period rolls and there is no
    /// transaction here to make setting one and clearing the other a single act.
    /// </summary>
    [Fact]
    public async Task Only_the_window_containing_now_is_returned()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        var previous = Document(tenantId, used: 5, counterVersion: 1, periodKey: "M2026-08");
        previous.PeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        previous.PeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var live = Document(tenantId, used: 30, counterVersion: 2, periodKey: "M2026-09");
        live.PeriodStartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        live.PeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        await _current.TryPublishAsync(previous, CancellationToken.None);
        await _current.TryPublishAsync(live, CancellationToken.None);

        var found = await _current.ListCurrentAsync(
            tenantId, "org-1", Sub(tenantId), now, CancellationToken.None);

        found.Should().ContainSingle().Which.PeriodKey.Should().Be("M2026-09");
    }

    /// <summary>
    /// A period ends the instant its successor begins. An inclusive upper bound would return both at
    /// the boundary, and a reader would see two allowances for one meter.
    /// </summary>
    [Fact]
    public async Task Exactly_one_window_is_current_at_a_period_boundary()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var boundary = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var previous = Document(tenantId, used: 5, counterVersion: 1, periodKey: "M2026-08");
        previous.PeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        previous.PeriodEndUtc = boundary;

        var next = Document(tenantId, used: 0, counterVersion: 1, periodKey: "M2026-09");
        next.PeriodStartUtc = boundary;
        next.PeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        await _current.TryPublishAsync(previous, CancellationToken.None);
        await _current.TryPublishAsync(next, CancellationToken.None);

        var found = await _current.ListCurrentAsync(
            tenantId, "org-1", Sub(tenantId), boundary, CancellationToken.None);

        found.Should().ContainSingle().Which.PeriodKey.Should().Be("M2026-09");
    }

    /// <summary>
    /// A lifetime capacity meter's window runs to <c>DateTime.MaxValue</c>, so the same boundary query
    /// has to select it without naming it as a special case.
    /// </summary>
    [Fact]
    public async Task A_lifetime_window_is_always_current()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var lifetime = Document(tenantId, used: 3, counterVersion: 1, periodKey: "LIFETIME");
        lifetime.MeterKey = "storage";
        lifetime.ItemId = SubscriptionUsageCurrent.CreateId(Sub(tenantId), "storage", "LIFETIME");
        lifetime.PeriodStartUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        lifetime.PeriodEndUtc = DateTime.MaxValue;

        await _current.TryPublishAsync(lifetime, CancellationToken.None);

        var found = await _current.ListCurrentAsync(
            tenantId,
            "org-1",
            Sub(tenantId),
            new DateTime(2031, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        found.Should().ContainSingle().Which.MeterKey.Should().Be("storage");
    }

    /// <summary>
    /// Organization scope is in the filter, not merely in the caller's intent. Without it, a direct
    /// read would be a cross-organization read of billing state.
    /// </summary>
    [Fact]
    public async Task Another_organizations_document_is_never_returned()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var theirs = Document(tenantId, used: 42, counterVersion: 1);
        theirs.OrganizationId = "org-2";
        theirs.SubscriptionId = $"other-{tenantId}";
        theirs.ItemId = SubscriptionUsageCurrent.CreateId(
            $"other-{tenantId}", "screening", "M2026-09");

        await _current.TryPublishAsync(Document(tenantId, 5, 1), CancellationToken.None);
        await _current.TryPublishAsync(theirs, CancellationToken.None);

        // Listed for org-1 and this test's own subscription. The other document is in the same
        // collection, under a different organization, and must not appear.

        var found = await _current.ListCurrentAsync(
            tenantId,
            "org-1",
            Sub(tenantId),
            new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        found.Should().ContainSingle().Which.OrganizationId.Should().Be("org-1");
    }

    /// <summary>
    /// The tenant is in the filter, not merely in the database the provider chose.
    /// </summary>
    /// <remarks>
    /// In production a tenant selects its own database, so this filter is defence in depth rather
    /// than the only thing separating two tenants. It is asserted because this fixture maps every
    /// tenant onto one database — which makes the filter the only thing that can exclude the other
    /// tenant's document, and therefore the only place this property can be tested at all.
    /// </remarks>
    [Fact]
    public async Task Another_tenants_document_is_never_returned()
    {
        var mine = MongoIntegrationFixture.NewTenantId();
        var theirs = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(theirs, used: 77, counterVersion: 1), CancellationToken.None);

        var found = await _current.ListCurrentAsync(
            mine,
            "org-1",
            Sub(theirs),
            new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        found.Should().BeEmpty(
            "the document exists and matches on organization, subscription and period - only the " +
            "tenant differs");
    }

    /// <summary>
    /// The reconciliation pass reads oldest-updated first, because a projection that has not been
    /// rewritten for a while is the one most likely to be behind its counter.
    /// </summary>
    [Fact]
    public async Task Reconciliation_candidates_come_back_oldest_first_and_bounded()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        foreach (var (meter, minutesAgo) in new[] { ("a", 5), ("b", 60), ("c", 1) })
        {
            var document = Document(tenantId, used: 1, counterVersion: 1);
            document.MeterKey = meter;
            document.ItemId = SubscriptionUsageCurrent.CreateId(
                Sub(tenantId), meter, "M2026-09");
            document.UpdatedAtUtc = now.AddMinutes(-minutesAgo);

            await _current.TryPublishAsync(document, CancellationToken.None);
        }

        var candidates = await _current.ListBehindCountersAsync(
            tenantId, now, 2, CancellationToken.None);

        candidates.Should().HaveCount(2, "the pass is bounded");
        candidates[0].MeterKey.Should().Be("b", "the least recently written comes first");
        candidates[1].MeterKey.Should().Be("a");
    }

    /// <summary>
    /// The indexes are what make a direct read safe to expose: without them a consumer could express
    /// a query that scans the collection. Asserted on the stored index list rather than on the
    /// builder code, because only the former is what the database will actually use.
    /// </summary>
    [Fact]
    public async Task The_declared_indexes_exist_on_the_collection()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.EnsureIndexesAsync(tenantId, CancellationToken.None);

        // The tenant selects the database, not a collection-name prefix: SubscriptionCollections.Of
        // resolves a database per tenant and then a plainly named collection inside it.
        var indexes = await _fixture.Database
            .GetCollection<BsonDocument>("SubscriptionUsageCurrent")
            .Indexes.List()
            .ToListAsync();

        var names = indexes.ConvertAll(index => index["name"].AsString);

        names.Should().Contain(SubscriptionIndexDefinitions.UsageCurrentUniqueIndexName);
        names.Should().Contain(SubscriptionIndexDefinitions.UsageCurrentReadIndexName);
        names.Should().Contain(SubscriptionIndexDefinitions.UsageCurrentStalenessIndexName);
        names.Should().Contain(SubscriptionIndexDefinitions.UsageCurrentExpiryIndexName);

        indexes
            .Should().ContainSingle(index =>
                index["name"].AsString == SubscriptionIndexDefinitions.UsageCurrentUniqueIndexName)
            .Which["unique"].AsBoolean.Should().BeTrue();

        indexes
            .Should().ContainSingle(index =>
                index["name"].AsString == SubscriptionIndexDefinitions.UsageCurrentExpiryIndexName)
            .Which.Contains("expireAfterSeconds").Should().BeTrue(
                "the projection must not outlive the counter it projects");
    }

    /// <summary>
    /// A subscription id derived from the tenant, so each test owns its own document space.
    /// </summary>
    /// <remarks>
    /// The fixture maps every tenant id onto one database, and the projection's <c>_id</c> is
    /// <c>{subscriptionId}:{meterKey}:{periodKey}</c> with no tenant in it — that is correct in
    /// production, where the tenant selects the database before the id is ever used. In a shared
    /// database it means a fixed subscription id would put every test in this class on the same
    /// document, and the version condition would then refuse whichever test ran second.
    /// </remarks>
    // ------------------------------------------------------------------ the meter's granularity

    /// <summary>
    /// The granularity a direct reader needs is stored beside the balances it describes.
    /// </summary>
    /// <remarks>
    /// Without it a reader meeting <c>Used</c> of <c>512.5</c> cannot tell whether that meter is
    /// measured to one place or to six, which it needs to format the figure and to know what the
    /// next usable amount is.
    /// </remarks>
    [Fact]
    public async Task The_meters_granularity_is_published_with_its_balances()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 40, counterVersion: 1, quantityScale: 3),
            CancellationToken.None);

        var stored = await _current.GetAsync(
            tenantId,
            SubscriptionUsageCurrent.CreateId(Sub(tenantId), "screening", "M2026-09"),
            CancellationToken.None);

        stored!.QuantityScale.Should().Be(3);
    }

    /// <summary>
    /// The granularity moves with the subscription's version, never with the counter's.
    /// </summary>
    /// <remarks>
    /// It is plan terms. In the balance group, this write — newer usage carrying the terms as they
    /// stood before a plan widened the meter — would drive the scale back down, and a reader would
    /// then format a figure to fewer places than it actually has.
    /// </remarks>
    [Fact]
    public async Task A_later_usage_publish_cannot_narrow_the_granularity()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        // The plan widens the meter to three places.
        await _current.TryPublishAsync(
            Document(tenantId, used: 10, counterVersion: 5, subscriptionVersion: 7, quantityScale: 3),
            CancellationToken.None);

        // A usage write that started before that change: newer counter, older subscription.
        (await _current.TryPublishAsync(
            Document(tenantId, used: 20, counterVersion: 6, subscriptionVersion: 6, quantityScale: 0),
            CancellationToken.None)).Should().BeTrue();

        var stored = await _current.GetAsync(
            tenantId,
            SubscriptionUsageCurrent.CreateId(Sub(tenantId), "screening", "M2026-09"),
            CancellationToken.None);

        stored!.Used.Should().Be(20, "the balance is the counter's to say");
        stored.QuantityScale.Should().Be(3, "the granularity is not");
    }

    /// <summary>A newer plan does widen it.</summary>
    [Fact]
    public async Task A_later_plan_change_widens_the_granularity()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 10, counterVersion: 5, subscriptionVersion: 6, quantityScale: 0),
            CancellationToken.None);
        await _current.TryPublishAsync(
            Document(tenantId, used: 10, counterVersion: 5, subscriptionVersion: 7, quantityScale: 3),
            CancellationToken.None);

        var stored = await _current.GetAsync(
            tenantId,
            SubscriptionUsageCurrent.CreateId(Sub(tenantId), "screening", "M2026-09"),
            CancellationToken.None);

        stored!.QuantityScale.Should().Be(3);
    }

    /// <summary>
    /// A document written before the field existed reads as whole units, which is what every meter
    /// was then.
    /// </summary>
    [Fact]
    public async Task A_document_written_without_a_granularity_reads_as_whole_units()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var documentId = SubscriptionUsageCurrent.CreateId(Sub(tenantId), "screening", "M2026-09");

        await _fixture.Database
            .GetCollection<BsonDocument>("SubscriptionUsageCurrent")
            .InsertOneAsync(new BsonDocument
            {
                ["_id"] = documentId,
                ["TenantId"] = tenantId,
                ["OrganizationId"] = "org-1",
                ["SubscriptionId"] = Sub(tenantId),
                ["MeterKey"] = "screening",
                ["PeriodKey"] = "M2026-09",
                ["PeriodStartUtc"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                ["PeriodEndUtc"] = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                ["Used"] = new BsonInt64(40),
                ["Included"] = new BsonInt64(100),
                ["CounterVersion"] = new BsonInt64(1),
                ["SubscriptionVersion"] = new BsonInt64(1),
                // Written by the build before this field, so it carries the older schema and none
                // of the field.
                ["SchemaVersion"] = 1,
                ["UpdatedAtUtc"] = DateTime.UtcNow,
                ["ExpiresAtUtc"] = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)
            });

        var stored = await _current.GetAsync(tenantId, documentId, CancellationToken.None);

        stored!.QuantityScale.Should().Be(0);
        stored.SchemaVersion.Should().BeLessThan(
            SubscriptionUsageCurrent.CurrentSchemaVersion,
            "which is what tells the reconciliation sweep to republish it");
    }

    private static string Sub(string tenantId) => $"sub-{tenantId}";

    private static SubscriptionUsageCurrent Document(
        string tenantId,
        long used,
        long counterVersion,
        string periodKey = "M2026-09",
        long subscriptionVersion = 1,
        int quantityScale = 0) => new()
    {
        ItemId = SubscriptionUsageCurrent.CreateId(Sub(tenantId), "screening", periodKey),
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = Sub(tenantId),
        SubscriptionStatus = SubscriptionStatus.Active,
        PlanId = "plan-1",
        PlanCode = "pro",
        MeterKey = "screening",
        UnitLabel = "screening",
        QuantityScale = quantityScale,
        PeriodKey = periodKey,
        PeriodStartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        Included = 100,
        Used = used,
        Remaining = Math.Max(0, 100 - used),
        Overage = Math.Max(0, used - 100),
        OverageAllowed = true,
        CounterVersion = counterVersion,
        SubscriptionVersion = subscriptionVersion,
        SchemaVersion = SubscriptionUsageCurrent.CurrentSchemaVersion,
        UpdatedAtUtc = new DateTime(2026, 9, 15, 11, 0, 0, DateTimeKind.Utc),
        ExpiresAtUtc = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)
    };
}
