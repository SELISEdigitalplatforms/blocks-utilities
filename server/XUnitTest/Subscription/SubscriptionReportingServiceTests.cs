using FluentAssertions;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Reporting;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// The arithmetic and the guards in the reporting service, with the database mocked away.
/// </summary>
/// <remarks>
/// What is worth testing here is what a reader of a report cannot check for themselves: that a
/// yearly price was divided by twelve and not by one, that a seat count landed in the band it
/// belongs to, that two currencies were never added together, and that a window nobody bounded
/// was refused before it reached the ledger. Each of those is wrong in a way that still looks
/// like a plausible number.
/// </remarks>
public sealed class SubscriptionReportingServiceTests
{
    private static readonly DateTime Now = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ISubscriptionReportingRepository> _reports = new(MockBehavior.Loose);
    private readonly Mock<ISubscriptionContextResolver> _context = new();
    private readonly SubscriptionReportingService _service;

    public SubscriptionReportingServiceTests()
    {
        // Every repository method returns an empty result unless a test says otherwise. A loose
        // mock hands back null, which the real repository never does, so without this the tests
        // would be asserting against a state the production code cannot be in.
        _reports
            .Setup(reports => reports.AggregateUsageAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _reports
            .Setup(reports => reports.ListByStatusAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<SubscriptionStatus>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _reports
            .Setup(reports => reports.AggregateDocumentRevenueAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _reports
            .Setup(reports => reports.AggregateOverageRevenueAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _reports
            .Setup(reports => reports.ListRetryingUsageInvoicesAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _reports
            .Setup(reports => reports.ListSubscriptionsAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<SubscriptionReportCursor?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionRosterPage([], false));

        _reports
            .Setup(reports => reports.ListCurrentUsageAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _reports
            .Setup(reports => reports.AggregateCouponUptakeAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _reports
            .Setup(reports => reports.AggregateCouponRevenueAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _reports
            .Setup(reports => reports.AggregateCampaignRedemptionsAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _reports
            .Setup(reports => reports.GetDiscountCodesAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        _context
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext("tenant-1", "org-1", "actor-1", "user-1")));

        _service = new SubscriptionReportingService(
            _reports.Object,
            _context.Object,
            new FakeClock(Now));
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(2, "2-3")]
    [InlineData(3, "2-3")]
    [InlineData(4, "4-9")]
    [InlineData(9, "4-9")]
    [InlineData(10, "10-24")]
    [InlineData(24, "10-24")]
    [InlineData(25, "25-40")]
    [InlineData(40, "25-40")]
    [InlineData(41, "41+")]
    [InlineData(4000, "41+")]
    public void Every_seat_count_lands_in_the_band_that_contains_it(long seats, string expected) =>
        SubscriptionRunRate.TierFor(seats).Name.Should().Be(
            expected,
            "a band boundary is off by one in whichever direction nobody checked");

    [Fact]
    public void A_seat_count_below_one_still_lands_in_a_band() =>
        SubscriptionRunRate.TierFor(0).Name.Should().Be(
            "1",
            "dropping it would make the tier counts fail to sum to the subscription count, " +
            "which is the one arithmetic check a reader can perform");

    [Theory]
    [InlineData(BillingInterval.Month, 1, 1_000, 1_000)]
    [InlineData(BillingInterval.Month, 3, 3_000, 1_000)]
    [InlineData(BillingInterval.Year, 1, 12_000, 1_000)]
    [InlineData(BillingInterval.Year, 2, 24_000, 1_000)]
    [InlineData(BillingInterval.Week, 1, 1_000, 4_333)]
    [InlineData(BillingInterval.Day, 1, 100, 3_042)]
    public void A_period_price_normalizes_to_a_month(
        BillingInterval interval,
        int intervalCount,
        long periodMinor,
        long expectedMonthlyMinor) =>
        SubscriptionRunRate.MonthlyAmountMinor(periodMinor, interval, intervalCount)
            .Should().Be(expectedMonthlyMinor);

    [Fact]
    public void A_malformed_interval_count_is_treated_as_one_rather_than_dividing_by_zero() =>
        SubscriptionRunRate.MonthlyAmountMinor(5_000, BillingInterval.Month, 0)
            .Should().Be(5_000);

    [Fact]
    public async Task Annual_run_rate_is_exactly_twelve_times_the_monthly_one()
    {
        GivenSubscriptions(Subscription("CHF", 10_000, BillingInterval.Month));

        var report = await WhenRecurringRevenue();

        var currency = report.Currencies.Single();

        currency.GrossAnnualMinor.Should().Be(
            currency.GrossMonthlyMinor * 12,
            "a reader comparing the two figures must never find them disagreeing");
    }

    [Fact]
    public async Task Two_currencies_are_reported_apart_and_never_added_together()
    {
        GivenSubscriptions(
            Subscription("CHF", 10_000, BillingInterval.Month),
            Subscription("EUR", 20_000, BillingInterval.Month));

        var report = await WhenRecurringRevenue();

        report.Currencies.Should().HaveCount(2);
        report.Currencies.Single(entry => entry.CurrencyCode == "CHF")
            .GrossMonthlyMinor.Should().Be(10_000);
        report.Currencies.Single(entry => entry.CurrencyCode == "EUR")
            .GrossMonthlyMinor.Should().Be(
                20_000,
                "there is no exchange rate anywhere in this module, so a combined total would " +
                "be an addition of unlike things");
    }

    [Fact]
    public async Task A_live_discount_lowers_the_net_run_rate_but_not_the_gross()
    {
        var subscription = Subscription("CHF", 10_000, BillingInterval.Month);

        subscription.Discount = new DiscountTerms
        {
            Code = "HALF",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 5_000,
            ExpiresAtUtc = Now.AddDays(30)
        };

        GivenSubscriptions(subscription);

        var currency = (await WhenRecurringRevenue()).Currencies.Single();

        currency.GrossMonthlyMinor.Should().Be(10_000);
        currency.NetMonthlyMinor.Should().Be(5_000);
    }

    [Fact]
    public async Task An_expired_discount_leaves_the_net_run_rate_at_list_price()
    {
        var subscription = Subscription("CHF", 10_000, BillingInterval.Month);

        subscription.Discount = new DiscountTerms
        {
            Code = "LAPSED",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 5_000,
            ExpiresAtUtc = Now.AddDays(-1)
        };

        GivenSubscriptions(subscription);

        var currency = (await WhenRecurringRevenue()).Currencies.Single();

        currency.NetMonthlyMinor.Should().Be(
            10_000,
            "the run-rate must agree with what renewal will actually charge");
    }

    [Fact]
    public async Task Every_band_is_reported_including_the_empty_ones()
    {
        GivenSubscriptions(Subscription("CHF", 10_000, BillingInterval.Month));

        var currency = (await WhenRecurringRevenue()).Currencies.Single();

        currency.Tiers.Should().HaveCount(
            SubscriptionRunRate.Tiers.Count,
            "a tier missing from the response cannot be told from a tier the report forgot");
        currency.Tiers.Sum(tier => tier.SubscriptionCount).Should().Be(1);
    }

    [Fact]
    public async Task Reversals_lower_the_net_usage_without_touching_the_consumed_figure()
    {
        _reports
            .Setup(reports => reports.AggregateUsageAsync(
                "tenant-1",
                null,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new("screenings", Now.Date, UsageEntryType.Consumption, 100m, 10),
                new("screenings", Now.Date, UsageEntryType.Reversal, -30m, 3),
                new("screenings", Now.Date, UsageEntryType.Grant, 500m, 1)
            ]);

        var result = await _service.GetUsageAsync(
            new GetUsageReportRequest(), "correlation-1", CancellationToken.None);

        var bucket = result.Value!.Buckets.Single();

        bucket.ConsumedQuantity.Should().Be(100m);
        bucket.ReversedQuantity.Should().Be(30m, "reversals are reported as a positive figure");
        bucket.NetQuantity.Should().Be(70m);
        bucket.GrantedQuantity.Should().Be(
            500m,
            "a grant raises an allowance and is not something anybody consumed");
        bucket.RecordCount.Should().Be(14);
    }

    [Fact]
    public async Task A_granularity_the_endpoint_does_not_understand_is_refused()
    {
        var result = await _service.GetUsageAsync(
            new GetUsageReportRequest { Granularity = "hour" },
            "correlation-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ValidationErrors.Should().ContainKey(nameof(GetUsageReportRequest.Granularity));
    }

    [Theory]
    [InlineData("DAY", "day")]
    [InlineData("Month", "month")]
    public async Task Granularity_is_matched_whatever_its_casing(string sent, string expected)
    {
        var result = await _service.GetUsageAsync(
            new GetUsageReportRequest { Granularity = sent },
            "correlation-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Granularity.Should().Be(expected);
    }

    [Fact]
    public async Task A_window_wider_than_a_year_is_refused_before_it_reaches_the_ledger()
    {
        var result = await _service.GetUsageAsync(
            new GetUsageReportRequest
            {
                FromUtc = Now.AddYears(-3),
                ToUtc = Now
            },
            "correlation-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);

        _reports.Verify(
            reports => reports.AggregateUsageAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "an unbounded range is a full ledger scan and must be refused, not merely slow");
    }

    [Fact]
    public async Task A_window_that_ends_before_it_starts_is_refused()
    {
        var result = await _service.GetUsageAsync(
            new GetUsageReportRequest { FromUtc = Now, ToUtc = Now.AddDays(-1) },
            "correlation-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
    }

    [Fact]
    public async Task An_unbounded_usage_request_defaults_to_the_current_month()
    {
        await _service.GetUsageAsync(
            new GetUsageReportRequest(), "correlation-1", CancellationToken.None);

        _reports.Verify(
            reports => reports.AggregateUsageAsync(
                "tenant-1",
                null,
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Credit_notes_are_reported_beside_the_revenue_and_never_subtracted_from_it()
    {
        _reports
            .Setup(reports => reports.AggregateDocumentRevenueAsync(
                "tenant-1",
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new("CHF", FinancialDocumentType.Invoice, 50_000, 5),
                new("CHF", FinancialDocumentType.TrialInvoice, 0, 2),
                new("CHF", FinancialDocumentType.CreditNote, 7_000, 1)
            ]);

        _reports
            .Setup(reports => reports.AggregateOverageRevenueAsync(
                "tenant-1",
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new("CHF", 12_000, 3)]);

        var result = await _service.GetRevenueAsync(
            new GetRevenueReportRequest(), "correlation-1", CancellationToken.None);

        var currency = result.Value!.Currencies.Single();

        currency.SubscriptionRevenueMinor.Should().Be(50_000);
        currency.OverageRevenueMinor.Should().Be(12_000);
        currency.TotalRevenueMinor.Should().Be(62_000);
        currency.CreditNoteMinor.Should().Be(
            7_000,
            "a credit note commonly refunds an earlier window and must not reduce this one");
        currency.SubscriptionDocumentCount.Should().Be(7);
    }

    [Fact]
    public async Task A_currency_present_only_in_overage_still_appears_in_the_revenue_report()
    {
        _reports
            .Setup(reports => reports.AggregateDocumentRevenueAsync(
                "tenant-1",
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new("CHF", FinancialDocumentType.Invoice, 50_000, 5)]);

        _reports
            .Setup(reports => reports.AggregateOverageRevenueAsync(
                "tenant-1",
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new("EUR", 12_000, 3)]);

        var result = await _service.GetRevenueAsync(
            new GetRevenueReportRequest(), "correlation-1", CancellationToken.None);

        result.Value!.Currencies.Should().HaveCount(2);
        result.Value.Currencies.Single(entry => entry.CurrencyCode == "EUR")
            .OverageRevenueMinor.Should().Be(12_000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task A_page_size_outside_the_bounds_is_refused(int pageSize)
    {
        var result = await _service.GetRosterAsync(
            new GetSubscriptionReportRequest { PageSize = pageSize },
            "correlation-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
    }

    [Fact]
    public async Task A_cursor_issued_to_another_tenant_is_refused()
    {
        var foreign = SubscriptionReportCursorCodec.Encode(
            "tenant-2", new SubscriptionReportCursor(Now, "subscription-1"));

        var result = await _service.GetRosterAsync(
            new GetSubscriptionReportRequest { After = foreign },
            "correlation-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse(
            "a cursor is a value the client holds and can edit, so one that is not refused " +
            "across tenants is an access-control bypass wearing base64");
        result.ValidationErrors.Should()
            .ContainKey(nameof(GetSubscriptionReportRequest.After));
    }

    [Fact]
    public async Task A_cursor_issued_to_this_tenant_round_trips()
    {
        var boundary = new SubscriptionReportCursor(Now, "subscription-1");

        _reports
            .Setup(reports => reports.ListSubscriptionsAsync(
                "tenant-1",
                25,
                It.Is<SubscriptionReportCursor>(cursor =>
                    cursor.SubscriptionId == "subscription-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionRosterPage([], false));

        var result = await _service.GetRosterAsync(
            new GetSubscriptionReportRequest
            {
                After = SubscriptionReportCursorCodec.Encode("tenant-1", boundary)
            },
            "correlation-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_subscription_with_no_published_usage_reports_no_meters_rather_than_zero()
    {
        _reports
            .Setup(reports => reports.ListSubscriptionsAsync(
                "tenant-1", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionRosterPage(
                [Subscription("CHF", 10_000, BillingInterval.Month)], false));

        _reports
            .Setup(reports => reports.ListCurrentUsageAsync(
                "tenant-1",
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _service.GetRosterAsync(
            new GetSubscriptionReportRequest(), "correlation-1", CancellationToken.None);

        result.Value!.Items.Single().Meters.Should().BeEmpty(
            "an absent projection and a genuinely unused meter mean opposite things, and only " +
            "one of them is a reason to act");
    }

    [Fact]
    public async Task Usage_against_an_unlimited_allowance_reports_no_percentage()
    {
        var subscription = Subscription("CHF", 10_000, BillingInterval.Month);

        _reports
            .Setup(reports => reports.ListSubscriptionsAsync(
                "tenant-1", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionRosterPage([subscription], false));

        _reports
            .Setup(reports => reports.ListCurrentUsageAsync(
                "tenant-1",
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubscriptionUsageCurrent
                {
                    SubscriptionId = subscription.ItemId,
                    MeterKey = "unlimited",
                    Included = 0m,
                    Used = 40m
                },
                new SubscriptionUsageCurrent
                {
                    SubscriptionId = subscription.ItemId,
                    MeterKey = "screenings",
                    Included = 200m,
                    Used = 50m,
                    Remaining = 150m
                }
            ]);

        var result = await _service.GetRosterAsync(
            new GetSubscriptionReportRequest(), "correlation-1", CancellationToken.None);

        var meters = result.Value!.Items.Single().Meters;

        meters.Single(meter => meter.MeterKey == "unlimited")
            .UsedPercentOfQuota.Should().BeNull(
                "a zero would read as 'nothing used', which is the opposite of what an " +
                "unlimited meter means");
        meters.Single(meter => meter.MeterKey == "screenings")
            .UsedPercentOfQuota.Should().Be(25m);
    }

    /// <summary>
    /// One row per meter, chosen — not every row the projection happens to hold.
    /// </summary>
    /// <remarks>
    /// Dev data carries a meter with a window that has just closed sitting beside the one running
    /// now. Listing both repeats the meter with different figures and leaves the reader to guess
    /// which is current, so the window containing now is the one reported.
    /// </remarks>
    [Fact]
    public async Task A_meter_reports_the_window_running_now_and_not_one_that_has_closed()
    {
        var subscription = Subscription("CHF", 10_000, BillingInterval.Month);

        _reports
            .Setup(reports => reports.ListSubscriptionsAsync(
                "tenant-1", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionRosterPage([subscription], false));

        _reports
            .Setup(reports => reports.ListCurrentUsageAsync(
                "tenant-1",
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                // Closed four days ago, but published more recently than the live one — so a
                // rule that only took the newest row would pick this and report stale figures.
                new SubscriptionUsageCurrent
                {
                    SubscriptionId = subscription.ItemId,
                    MeterKey = "screenings",
                    PeriodKey = "closed",
                    PeriodStartUtc = Now.AddDays(-10),
                    PeriodEndUtc = Now.AddDays(-4),
                    Included = 200m,
                    Used = 5m,
                    UpdatedAtUtc = Now
                },
                new SubscriptionUsageCurrent
                {
                    SubscriptionId = subscription.ItemId,
                    MeterKey = "screenings",
                    PeriodKey = "running",
                    PeriodStartUtc = Now.AddDays(-2),
                    PeriodEndUtc = Now.AddDays(28),
                    Included = 200m,
                    Used = 120m,
                    UpdatedAtUtc = Now.AddDays(-1)
                }
            ]);

        var result = await _service.GetRosterAsync(
            new GetSubscriptionReportRequest(), "correlation-1", CancellationToken.None);

        var meter = result.Value!.Items.Single().Meters.Should().ContainSingle(
            "a meter listed twice with different figures cannot be read").Subject;

        meter.PeriodKey.Should().Be("running");
        meter.Used.Should().Be(120m);
    }

    /// <summary>
    /// Where no window contains now, the most recently published row is the best answer available.
    /// </summary>
    /// <remarks>
    /// This is the shape a legacy row takes: the projection holds two rows for the same period
    /// because a missing <c>UserId</c> and an empty one are distinct keys to the unique index that
    /// would otherwise have refused the second.
    /// </remarks>
    [Fact]
    public async Task Two_rows_for_one_period_collapse_to_the_most_recently_published()
    {
        var subscription = Subscription("CHF", 10_000, BillingInterval.Month);

        _reports
            .Setup(reports => reports.ListSubscriptionsAsync(
                "tenant-1", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionRosterPage([subscription], false));

        _reports
            .Setup(reports => reports.ListCurrentUsageAsync(
                "tenant-1",
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubscriptionUsageCurrent
                {
                    SubscriptionId = subscription.ItemId,
                    MeterKey = "screenings",
                    PeriodKey = "same",
                    PeriodStartUtc = Now.AddDays(-30),
                    PeriodEndUtc = Now.AddDays(-2),
                    Included = 200m,
                    Used = 10m,
                    UpdatedAtUtc = Now.AddDays(-9)
                },
                new SubscriptionUsageCurrent
                {
                    SubscriptionId = subscription.ItemId,
                    MeterKey = "screenings",
                    PeriodKey = "same",
                    PeriodStartUtc = Now.AddDays(-30),
                    PeriodEndUtc = Now.AddDays(-2),
                    Included = 200m,
                    Used = 80m,
                    UpdatedAtUtc = Now.AddDays(-1)
                }
            ]);

        var result = await _service.GetRosterAsync(
            new GetSubscriptionReportRequest(), "correlation-1", CancellationToken.None);

        result.Value!.Items.Single().Meters.Should().ContainSingle()
            .Which.Used.Should().Be(80m);
    }

    [Fact]
    public async Task A_standard_coupon_is_counted_even_though_it_has_no_redemption_row()
    {
        _reports
            .Setup(reports => reports.AggregateCouponUptakeAsync(
                "tenant-1",
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new("SPRING", "org-1", 2),
                new("SPRING", "org-2", 1)
            ]);

        _reports
            .Setup(reports => reports.AggregateCouponRevenueAsync(
                "tenant-1",
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new("SPRING", "CHF", 30_000, 6_000, 3)]);

        _reports
            .Setup(reports => reports.AggregateCampaignRedemptionsAsync(
                "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _service.GetCouponsAsync(
            new GetRevenueReportRequest(), "correlation-1", CancellationToken.None);

        var coupon = result.Value!.Coupons.Single();

        coupon.Code.Should().Be("SPRING");
        coupon.RedeemingOrganizations.Should().Be(
            2,
            "counting off the campaign redemption ledger would report zero for every ordinary " +
            "promotional code, which is a wrong answer that looks like a real one");
        coupon.SubscriptionCount.Should().Be(3);
        coupon.CampaignStates.Should().BeEmpty(
            "an ordinary code has no one-use rule to enforce and so nothing to reserve");
        coupon.Currencies.Single().RevenueMinor.Should().Be(30_000);
        coupon.Currencies.Single().DiscountGivenMinor.Should().Be(6_000);
    }

    [Fact]
    public async Task Campaign_redemptions_are_reported_under_their_discount_code()
    {
        _reports
            .Setup(reports => reports.AggregateCouponUptakeAsync(
                "tenant-1",
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new("LAUNCH", "org-1", 1)]);

        _reports
            .Setup(reports => reports.AggregateCampaignRedemptionsAsync(
                "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new("discount-1", CampaignRedemptionState.Redeemed, 4)]);

        _reports
            .Setup(reports => reports.GetDiscountCodesAsync(
                "tenant-1",
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["discount-1"] = "LAUNCH" });

        var result = await _service.GetCouponsAsync(
            new GetRevenueReportRequest(), "correlation-1", CancellationToken.None);

        var coupon = result.Value!.Coupons.Single();

        coupon.Code.Should().Be("LAUNCH");
        coupon.CampaignStates.Single().State.Should().Be("Redeemed");
        coupon.CampaignStates.Single().Count.Should().Be(4);
    }

    [Fact]
    public async Task A_redemption_whose_discount_was_deleted_is_still_reported()
    {
        _reports
            .Setup(reports => reports.AggregateCouponUptakeAsync(
                "tenant-1",
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _reports
            .Setup(reports => reports.AggregateCampaignRedemptionsAsync(
                "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new("discount-gone", CampaignRedemptionState.Redeemed, 2)]);

        _reports
            .Setup(reports => reports.GetDiscountCodesAsync(
                "tenant-1",
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        var result = await _service.GetCouponsAsync(
            new GetRevenueReportRequest(), "correlation-1", CancellationToken.None);

        result.Value!.Coupons.Single().Code.Should().Be(
            "discount-gone",
            "a campaign that was removed still accounts for the subscriptions it created");
    }

    [Fact]
    public async Task An_unresolvable_caller_gets_the_context_failure_and_no_query_runs()
    {
        _context
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Unresolved(
                PaymentFailureKind.Unauthenticated,
                "subscription_organization_missing",
                "No organization."));

        var result = await _service.GetDunningAsync("correlation-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Unauthenticated);

        _reports.Verify(
            reports => reports.ListByStatusAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<SubscriptionStatus>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task The_dunning_report_asks_only_for_the_statuses_that_owe_money()
    {
        await _service.GetDunningAsync("correlation-1", CancellationToken.None);

        _reports.Verify(
            reports => reports.ListByStatusAsync(
                "tenant-1",
                It.Is<IReadOnlyCollection<SubscriptionStatus>>(statuses =>
                    statuses.Contains(SubscriptionStatus.PastDue) &&
                    statuses.Contains(SubscriptionStatus.Unpaid) &&
                    statuses.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void GivenSubscriptions(params SubscriptionDetail[] subscriptions) =>
        _reports
            .Setup(reports => reports.ListByStatusAsync(
                "tenant-1",
                It.IsAny<IReadOnlyCollection<SubscriptionStatus>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriptions);

    private async Task<RecurringRevenueReportResponse> WhenRecurringRevenue()
    {
        var result = await _service.GetRecurringRevenueAsync(
            "correlation-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        return result.Value!;
    }

    private static SubscriptionDetail Subscription(
        string currencyCode,
        long unitAmountMinor,
        BillingInterval interval,
        int intervalCount = 1) =>
        new()
        {
            TenantId = "tenant-1",
            OrganizationId = "org-1",
            CurrencyCode = currencyCode,
            Status = SubscriptionStatus.Active,
            Plan = new PlanSnapshot { Code = "pro", DisplayName = "Pro" },
            Price = new PriceSnapshot
            {
                CurrencyCode = currencyCode,
                UnitAmountMinor = unitAmountMinor,
                Interval = interval,
                IntervalCount = intervalCount
            }
        };

    private sealed class FakeClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
