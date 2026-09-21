using FluentAssertions;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Reporting;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The reporting aggregations, against a real MongoDB.
/// </summary>
/// <remarks>
/// Every method on this repository is a server-side aggregation pipeline. What can go wrong is
/// the translation: a group key the driver cannot express, a date part it will not project, a
/// nested field path that silently matches nothing. None of that is visible to a mock — mocking it
/// would test the mock — and all of it fails at the first real call rather than at compile time.
/// <para>
/// The other thing proved here is tenant isolation. Reporting is the only code in this module that
/// deliberately reads across organizations, so the tenant filter stops being a side effect of the
/// query shape and becomes the sole thing keeping one merchant's commercial position away from
/// another's. That is worth a test that puts two tenants in one database and asks.
/// </para>
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriptionReportingRepositoryIntegrationTests
{
    // Named here as literals, the way every other integration test in this project does. The
    // repository resolves them through an internal type this assembly cannot see, and restating
    // them is also the point: if a collection is ever renamed, this test fails rather than
    // silently reporting on an empty collection.
    private const string UsageRecords = "SubscriptionUsageRecords";
    private const string FinancialDocuments = "SubscriptionFinancialDocuments";
    private const string UsageInvoices = "SubscriptionUsageInvoices";
    private const string Subscriptions = "Subscriptions";
    private const string CampaignRedemptions = "SubscriptionCampaignRedemptions";
    private const string Discounts = "SubscriptionDiscounts";

    private static readonly DateTime March = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime February = new(2026, 2, 14, 9, 0, 0, DateTimeKind.Utc);

    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionReportingRepository _reports;

    public SubscriptionReportingRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _reports = new SubscriptionReportingRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task Usage_is_grouped_by_meter_month_and_entry_type()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await InsertUsageAsync(
            UsageRecord(tenantId, "screenings", March, UsageEntryType.Consumption, 40m),
            UsageRecord(tenantId, "screenings", March.AddDays(1), UsageEntryType.Consumption, 60m),
            UsageRecord(tenantId, "screenings", March.AddDays(2), UsageEntryType.Reversal, -25m),
            UsageRecord(tenantId, "screenings", March, UsageEntryType.Grant, 500m),
            UsageRecord(tenantId, "seats", March, UsageEntryType.Consumption, 7m),
            UsageRecord(tenantId, "screenings", February, UsageEntryType.Consumption, 15m));

        var buckets = await _reports.AggregateUsageAsync(
            tenantId,
            meterKey: null,
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            byMonth: true,
            CancellationToken.None);

        var marchStart = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        Quantity(buckets, "screenings", marchStart, UsageEntryType.Consumption)
            .Should().Be(100m, "the two March consumption records fall in one month bucket");
        Quantity(buckets, "screenings", marchStart, UsageEntryType.Reversal)
            .Should().Be(-25m, "a reversal is stored with a negative delta");
        Quantity(buckets, "screenings", marchStart, UsageEntryType.Grant)
            .Should().Be(
                500m,
                "a grant must stay a separate figure, never folded into consumption");
        Quantity(buckets, "seats", marchStart, UsageEntryType.Consumption)
            .Should().Be(7m, "each meter is its own bucket");
        Quantity(
                buckets,
                "screenings",
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                UsageEntryType.Consumption)
            .Should().Be(15m, "month over month is one call for a two-month window");
    }

    [Fact]
    public async Task Daily_granularity_keeps_each_day_apart()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await InsertUsageAsync(
            UsageRecord(tenantId, "screenings", March, UsageEntryType.Consumption, 40m),
            UsageRecord(tenantId, "screenings", March.AddDays(1), UsageEntryType.Consumption, 60m));

        var buckets = await _reports.AggregateUsageAsync(
            tenantId,
            meterKey: null,
            March.Date,
            March.Date.AddDays(7),
            byMonth: false,
            CancellationToken.None);

        buckets.Should().HaveCount(2);
        buckets.Select(bucket => bucket.BucketStartUtc)
            .Should().BeInAscendingOrder("a chart reads the buckets in the order they arrive");
    }

    [Fact]
    public async Task A_meter_key_narrows_the_usage_report_to_that_meter()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await InsertUsageAsync(
            UsageRecord(tenantId, "screenings", March, UsageEntryType.Consumption, 40m),
            UsageRecord(tenantId, "seats", March, UsageEntryType.Consumption, 7m));

        var buckets = await _reports.AggregateUsageAsync(
            tenantId,
            "screenings",
            March.Date,
            March.Date.AddDays(1),
            byMonth: true,
            CancellationToken.None);

        buckets.Should().ContainSingle()
            .Which.MeterKey.Should().Be("screenings");
    }

    [Fact]
    public async Task One_tenants_usage_never_appears_in_another_tenants_report()
    {
        var mine = MongoIntegrationFixture.NewTenantId();
        var theirs = MongoIntegrationFixture.NewTenantId();

        await InsertUsageAsync(
            UsageRecord(mine, "screenings", March, UsageEntryType.Consumption, 40m),
            UsageRecord(theirs, "screenings", March, UsageEntryType.Consumption, 9_000m));

        var buckets = await _reports.AggregateUsageAsync(
            mine,
            meterKey: null,
            March.Date,
            March.Date.AddDays(1),
            byMonth: true,
            CancellationToken.None);

        buckets.Sum(bucket => bucket.Quantity).Should().Be(
            40m,
            "reporting is the only code here that reads across organizations, so the tenant " +
            "filter is the only thing keeping the two books apart");
    }

    [Fact]
    public async Task Document_revenue_is_grouped_by_currency_and_document_type()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await InsertDocumentsAsync(
            Document(tenantId, "CHF", FinancialDocumentType.Invoice, March, 50_000),
            Document(tenantId, "CHF", FinancialDocumentType.Invoice, March.AddDays(1), 10_000),
            Document(tenantId, "CHF", FinancialDocumentType.CreditNote, March, 7_000),
            Document(tenantId, "EUR", FinancialDocumentType.Invoice, March, 20_000),
            Document(tenantId, "CHF", FinancialDocumentType.Invoice, February, 99_000));

        var buckets = await _reports.AggregateDocumentRevenueAsync(
            tenantId,
            March.Date,
            March.Date.AddDays(7),
            CancellationToken.None);

        buckets.Single(bucket =>
                bucket.CurrencyCode == "CHF" &&
                bucket.DocumentType == FinancialDocumentType.Invoice)
            .TotalMinor.Should().Be(60_000);
        buckets.Single(bucket =>
                bucket.CurrencyCode == "CHF" &&
                bucket.DocumentType == FinancialDocumentType.CreditNote)
            .TotalMinor.Should().Be(7_000);
        buckets.Single(bucket => bucket.CurrencyCode == "EUR")
            .TotalMinor.Should().Be(
                20_000,
                "two currencies must survive as two rows and never be added together");
        buckets.Sum(bucket => bucket.TotalMinor).Should().Be(
            87_000,
            "February falls outside the window");
    }

    [Fact]
    public async Task Overage_revenue_counts_only_invoices_that_actually_charged()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await InsertUsageInvoicesAsync(
            UsageInvoice(tenantId, SubscriptionUsageInvoiceState.Charged, March, 12_000),
            UsageInvoice(tenantId, SubscriptionUsageInvoiceState.Pending, March, 99_000),
            UsageInvoice(tenantId, SubscriptionUsageInvoiceState.Abandoned, March, 99_000));

        var buckets = await _reports.AggregateOverageRevenueAsync(
            tenantId,
            March.Date,
            March.Date.AddDays(7),
            CancellationToken.None);

        buckets.Single().TotalMinor.Should().Be(
            12_000,
            "a pending invoice is money hoped for, not money earned");
    }

    [Fact]
    public async Task The_dunning_list_holds_only_invoices_that_have_already_been_tried()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var untried = UsageInvoice(tenantId, SubscriptionUsageInvoiceState.Pending, March, 5_000);
        untried.AttemptCount = 0;

        var failing = UsageInvoice(tenantId, SubscriptionUsageInvoiceState.Pending, March, 6_000);
        failing.AttemptCount = 3;

        await InsertUsageInvoicesAsync(untried, failing);

        var invoices = await _reports.ListRetryingUsageInvoicesAsync(
            tenantId, 200, CancellationToken.None);

        invoices.Should().ContainSingle()
            .Which.TotalAmountMinor.Should().Be(
                6_000,
                "an invoice awaiting its first attempt is not yet failing to collect");
    }

    [Fact]
    public async Task Coupon_uptake_is_read_from_subscriptions_so_standard_codes_are_counted()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        // org-1 subscribed twice: an earlier subscription that has since been cancelled, and the
        // live one. Both count as uptake — a coupon that was redeemed and later churned was still
        // redeemed. They cannot both be live, because the reservation index permits an
        // organization only one subscription at a time.
        var churned = WithDiscount(Subscription(tenantId, "org-1", March), "SPRING");
        churned.Status = SubscriptionStatus.Canceled;

        await InsertSubscriptionsAsync(
            churned,
            WithDiscount(Subscription(tenantId, "org-1", March.AddDays(1)), "SPRING"),
            WithDiscount(Subscription(tenantId, "org-2", March), "SPRING"),
            Subscription(tenantId, "org-3", March));

        var buckets = await _reports.AggregateCouponUptakeAsync(
            tenantId,
            March.Date,
            March.Date.AddDays(7),
            CancellationToken.None);

        buckets.Should().HaveCount(
            2,
            "one row per organization, so distinct organizations can be counted without " +
            "pulling every subscription back");
        buckets.Sum(bucket => bucket.SubscriptionCount).Should().Be(3);
        buckets.Should().OnlyContain(bucket => bucket.Code == "SPRING");
    }

    [Fact]
    public async Task A_subscription_carrying_no_coupon_is_excluded_rather_than_grouped_under_null()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await InsertSubscriptionsAsync(Subscription(tenantId, "org-1", March));

        var buckets = await _reports.AggregateCouponUptakeAsync(
            tenantId,
            March.Date,
            March.Date.AddDays(7),
            CancellationToken.None);

        buckets.Should().BeEmpty(
            "a missing Discount compares equal to null in Mongo, which is what the predicate " +
            "relies on to exclude it");
    }

    [Fact]
    public async Task Coupon_revenue_is_grouped_by_promotion_code_and_currency()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var discounted = Document(tenantId, "CHF", FinancialDocumentType.Invoice, March, 30_000);
        discounted.Amounts.PromotionCode = "SPRING";
        discounted.Amounts.PromotionalDiscountMinor = 6_000;

        var plain = Document(tenantId, "CHF", FinancialDocumentType.Invoice, March, 40_000);

        await InsertDocumentsAsync(discounted, plain);

        var buckets = await _reports.AggregateCouponRevenueAsync(
            tenantId,
            March.Date,
            March.Date.AddDays(7),
            CancellationToken.None);

        var bucket = buckets.Should().ContainSingle().Subject;

        bucket.Code.Should().Be("SPRING");
        bucket.RevenueMinor.Should().Be(30_000);
        bucket.DiscountGivenMinor.Should().Be(6_000);
        bucket.DocumentCount.Should().Be(1);
    }

    [Fact]
    public async Task Campaign_redemptions_are_grouped_by_discount_and_state()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _fixture
            .Collection<CampaignRedemption>(CampaignRedemptions)
            .InsertManyAsync(
            [
                Redemption(tenantId, "discount-1", CampaignRedemptionState.Redeemed),
                Redemption(tenantId, "discount-1", CampaignRedemptionState.Redeemed),
                Redemption(tenantId, "discount-1", CampaignRedemptionState.Released)
            ]);

        var buckets = await _reports.AggregateCampaignRedemptionsAsync(
            tenantId, CancellationToken.None);

        buckets.Single(bucket => bucket.State == CampaignRedemptionState.Redeemed)
            .Count.Should().Be(2);
        buckets.Single(bucket => bucket.State == CampaignRedemptionState.Released)
            .Count.Should().Be(1);
    }

    [Fact]
    public async Task Discount_codes_come_back_keyed_by_the_id_redemptions_hold()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var discountId = Guid.NewGuid().ToString();

        await _fixture
            .Collection<Discount>(Discounts)
            .InsertOneAsync(new Discount
            {
                ItemId = discountId,
                TenantId = tenantId,
                Code = "LAUNCH"
            });

        var codes = await _reports.GetDiscountCodesAsync(
            tenantId, [discountId], CancellationToken.None);

        codes[discountId].Should().Be("LAUNCH");
    }

    [Fact]
    public async Task The_roster_pages_by_cursor_without_repeating_or_skipping_a_subscription()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await InsertSubscriptionsAsync(
            [.. Enumerable.Range(0, 5).Select(
                index => Subscription(tenantId, $"org-{index}", March.AddMinutes(index)))]);

        var first = await _reports.ListSubscriptionsAsync(
            tenantId, 2, null, CancellationToken.None);

        first.Items.Should().HaveCount(2);
        first.HasMore.Should().BeTrue();

        var last = first.Items[^1];

        var second = await _reports.ListSubscriptionsAsync(
            tenantId,
            2,
            new SubscriptionReportCursor(last.CreatedAtUtc, last.ItemId),
            CancellationToken.None);

        second.Items.Should().HaveCount(2);
        second.Items.Select(item => item.ItemId)
            .Should().NotIntersectWith(first.Items.Select(item => item.ItemId));

        var third = await _reports.ListSubscriptionsAsync(
            tenantId,
            2,
            new SubscriptionReportCursor(
                second.Items[^1].CreatedAtUtc, second.Items[^1].ItemId),
            CancellationToken.None);

        third.Items.Should().HaveCount(1);
        third.HasMore.Should().BeFalse();

        first.Items.Concat(second.Items).Concat(third.Items)
            .Select(item => item.ItemId)
            .Should().OnlyHaveUniqueItems()
            .And.HaveCount(5, "keyset paging must reach every row exactly once");
    }

    [Fact]
    public async Task The_reporting_indexes_are_created_on_first_use()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _reports.AggregateUsageAsync(
            tenantId,
            meterKey: null,
            March.Date,
            March.Date.AddDays(1),
            byMonth: true,
            CancellationToken.None);

        var usageIndexes = await IndexNamesAsync<SubscriptionUsageRecord>(
            UsageRecords);

        usageIndexes.Should().Contain(
            SubscriptionIndexDefinitions.UsageRecordReportingIndexName,
            "without it a tenant-wide usage report is a collection scan of the largest " +
            "collection in the module");

        await _reports.AggregateDocumentRevenueAsync(
            tenantId, March.Date, March.Date.AddDays(1), CancellationToken.None);

        var documentIndexes = await IndexNamesAsync<SubscriptionFinancialDocument>(
            FinancialDocuments);

        documentIndexes.Should().Contain(
            SubscriptionIndexDefinitions.FinancialDocumentReportingIndexName);
    }

    private async Task<IReadOnlyList<string>> IndexNamesAsync<TDocument>(string collectionName)
    {
        var cursor = await _fixture.Collection<TDocument>(collectionName)
            .Indexes.ListAsync(CancellationToken.None);

        var indexes = await cursor.ToListAsync(CancellationToken.None);

        return [.. indexes.Select(index => index["name"].AsString)];
    }

    private static decimal Quantity(
        IEnumerable<UsageLedgerBucket> buckets,
        string meterKey,
        DateTime bucketStartUtc,
        UsageEntryType entryType) =>
        buckets
            .Where(bucket =>
                bucket.MeterKey == meterKey &&
                bucket.BucketStartUtc == bucketStartUtc &&
                bucket.EntryType == entryType)
            .Sum(bucket => bucket.Quantity);

    private Task InsertUsageAsync(params SubscriptionUsageRecord[] records) =>
        _fixture.Collection<SubscriptionUsageRecord>(UsageRecords)
            .InsertManyAsync(records);

    private Task InsertDocumentsAsync(params SubscriptionFinancialDocument[] documents) =>
        _fixture
            .Collection<SubscriptionFinancialDocument>(
                FinancialDocuments)
            .InsertManyAsync(documents);

    private Task InsertUsageInvoicesAsync(params SubscriptionUsageInvoice[] invoices) =>
        _fixture.Collection<SubscriptionUsageInvoice>(UsageInvoices)
            .InsertManyAsync(invoices);

    private Task InsertSubscriptionsAsync(params SubscriptionDetail[] subscriptions) =>
        _fixture.Collection<SubscriptionDetail>(Subscriptions)
            .InsertManyAsync(subscriptions);

    private static SubscriptionUsageRecord UsageRecord(
        string tenantId,
        string meterKey,
        DateTime occurredAtUtc,
        UsageEntryType entryType,
        decimal delta) =>
        new()
        {
            TenantId = tenantId,
            OrganizationId = "org-1",
            SubscriptionId = "subscription-1",
            MeterKey = meterKey,
            PeriodKey = occurredAtUtc.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            EntryType = entryType,
            Delta = delta,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = occurredAtUtc
        };

    private static SubscriptionFinancialDocument Document(
        string tenantId,
        string currencyCode,
        FinancialDocumentType documentType,
        DateTime issuedAtUtc,
        long totalMinor) =>
        new()
        {
            TenantId = tenantId,
            OrganizationId = "org-1",
            SubscriptionId = "subscription-1",
            DocumentNumber = Guid.NewGuid().ToString("N"),
            SourceKey = Guid.NewGuid().ToString("N"),
            DocumentType = documentType,
            CurrencyCode = currencyCode,
            IssuedAtUtc = issuedAtUtc,
            Amounts = new FinancialDocumentAmounts { TotalMinor = totalMinor }
        };

    private static SubscriptionUsageInvoice UsageInvoice(
        string tenantId,
        SubscriptionUsageInvoiceState state,
        DateTime updatedAtUtc,
        long totalMinor) =>
        new()
        {
            TenantId = tenantId,
            OrganizationId = "org-1",
            SubscriptionId = "subscription-1",
            PeriodKey = "2026-03",
            CurrencyCode = "CHF",
            State = state,
            TotalAmountMinor = totalMinor,
            AttemptCount = 1,
            CreatedAtUtc = updatedAtUtc,
            LastUpdatedDateUtc = updatedAtUtc
        };

    private static SubscriptionDetail Subscription(
        string tenantId,
        string organizationId,
        DateTime createdAtUtc) =>
        new()
        {
            TenantId = tenantId,
            OrganizationId = organizationId,
            CurrencyCode = "CHF",
            Status = SubscriptionStatus.Active,
            Plan = new PlanSnapshot { Code = "pro", DisplayName = "Pro" },
            Price = new PriceSnapshot
            {
                CurrencyCode = "CHF",
                UnitAmountMinor = 10_000,
                Interval = BillingInterval.Month,
                IntervalCount = 1
            },
            CreatedAtUtc = createdAtUtc
        };

    private static SubscriptionDetail WithDiscount(SubscriptionDetail subscription, string code)
    {
        subscription.Discount = new DiscountTerms { Code = code };

        return subscription;
    }

    private static CampaignRedemption Redemption(
        string tenantId,
        string discountId,
        CampaignRedemptionState state) =>
        new()
        {
            TenantId = tenantId,
            OrganizationId = Guid.NewGuid().ToString("N"),
            DiscountId = discountId,
            SubscriptionId = Guid.NewGuid().ToString("N"),
            State = state,
            ReservedAtUtc = March
        };
}
