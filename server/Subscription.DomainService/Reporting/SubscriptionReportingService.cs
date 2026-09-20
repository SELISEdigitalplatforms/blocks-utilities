using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace Subscription.DomainService.Reporting;

/// <summary>
/// Turns the reporting repository's buckets into answers, for the caller's own tenant.
/// </summary>
/// <remarks>
/// Validation happens here rather than in the repository, because the thing being protected is the
/// database: an unbounded date range is a full ledger scan, and the only place with both the
/// request and the authority to refuse it is this one.
/// </remarks>
public sealed class SubscriptionReportingService : ISubscriptionReportingService
{
    /// <summary>
    /// The widest window a report may cover.
    /// </summary>
    /// <remarks>
    /// A year plus a day, so that "the last twelve months" and a leap year both fit and nobody has
    /// to discover the limit by having a request refused. Beyond that the query stops being a
    /// report and becomes an export, which is a different thing with different costs.
    /// </remarks>
    private const int MaximumRangeDays = 366;

    private const int MaximumPageSize = 100;

    /// <summary>
    /// How many retrying usage invoices the dunning report returns.
    /// </summary>
    /// <remarks>
    /// Capped because a tenant whose payment provider is down has every invoice in this list, and
    /// a report that becomes unreturnable exactly when things are worst is no use to anybody. The
    /// count beside the list is not capped, so the size of the problem is still visible.
    /// </remarks>
    private const int MaximumDunningInvoices = 200;

    private const string DayGranularity = "day";
    private const string MonthGranularity = "month";

    private static readonly SubscriptionStatus[] LiveStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active,
        SubscriptionStatus.PastDue
    ];

    private static readonly SubscriptionStatus[] DunningStatuses =
    [
        SubscriptionStatus.PastDue,
        SubscriptionStatus.Unpaid
    ];

    private readonly ISubscriptionReportingRepository _reports;
    private readonly ISubscriptionContextResolver _context;
    private readonly TimeProvider _time;

    public SubscriptionReportingService(
        ISubscriptionReportingRepository reports,
        ISubscriptionContextResolver context,
        TimeProvider time)
    {
        _reports = reports;
        _context = context;
        _time = time;
    }

    public async Task<SubscriptionOperationResult<UsageReportResponse>> GetUsageAsync(
        GetUsageReportRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Matched case-insensitively and then replaced by the canonical constant, so what comes
        // back in the response is one of two fixed strings rather than whatever casing was sent.
        var requested = request.Granularity ?? MonthGranularity;

        var granularity =
            string.Equals(requested, DayGranularity, StringComparison.OrdinalIgnoreCase)
                ? DayGranularity
                : string.Equals(requested, MonthGranularity, StringComparison.OrdinalIgnoreCase)
                    ? MonthGranularity
                    : requested;

        if (granularity is not (DayGranularity or MonthGranularity))
        {
            return Invalid<UsageReportResponse>(
                correlationId,
                nameof(request.Granularity),
                $"Granularity must be '{DayGranularity}' or '{MonthGranularity}'.");
        }

        if (!TryResolveWindow(
                request.FromUtc,
                request.ToUtc,
                correlationId,
                nameof(request.FromUtc),
                out var window,
                out var windowFailure))
        {
            return windowFailure!.ToFailure<UsageReportResponse>();
        }

        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<UsageReportResponse>(correlationId);
        }

        var buckets = await _reports.AggregateUsageAsync(
            context.TenantId,
            request.MeterKey,
            window.FromUtc,
            window.ToUtc,
            granularity == MonthGranularity,
            cancellationToken);

        return SubscriptionOperationResult<UsageReportResponse>.Success(
            new UsageReportResponse
            {
                FromUtc = window.FromUtc,
                ToUtc = window.ToUtc,
                Granularity = granularity,
                Buckets = MapUsageBuckets(buckets)
            },
            correlationId);
    }

    public async Task<SubscriptionOperationResult<RecurringRevenueReportResponse>>
        GetRecurringRevenueAsync(
            string correlationId,
            CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<RecurringRevenueReportResponse>(correlationId);
        }

        var subscriptions = await _reports.ListByStatusAsync(
            context.TenantId, LiveStatuses, cancellationToken);

        var nowUtc = _time.GetUtcNow().UtcDateTime;

        var runRates = subscriptions
            .Select(subscription => RunRateOf(subscription, nowUtc))
            .ToList();

        var currencies = runRates
            .GroupBy(entry => entry.CurrencyCode, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new RecurringRevenueCurrencyResponse
            {
                CurrencyCode = group.Key,
                GrossMonthlyMinor = group.Sum(entry => entry.GrossMonthlyMinor),
                NetMonthlyMinor = group.Sum(entry => entry.NetMonthlyMinor),
                GrossAnnualMinor = SubscriptionRunRate.AnnualFromMonthly(
                    group.Sum(entry => entry.GrossMonthlyMinor)),
                NetAnnualMinor = SubscriptionRunRate.AnnualFromMonthly(
                    group.Sum(entry => entry.NetMonthlyMinor)),
                SubscriptionCount = group.LongCount(),
                Tiers = MapTiers(group)
            })
            .ToList();

        return SubscriptionOperationResult<RecurringRevenueReportResponse>.Success(
            new RecurringRevenueReportResponse
            {
                SubscriptionCount = subscriptions.Count,
                Currencies = currencies
            },
            correlationId);
    }

    public async Task<SubscriptionOperationResult<RevenueReportResponse>> GetRevenueAsync(
        GetRevenueReportRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryResolveWindow(
                request.FromUtc,
                request.ToUtc,
                correlationId,
                nameof(request.FromUtc),
                out var window,
                out var windowFailure))
        {
            return windowFailure!.ToFailure<RevenueReportResponse>();
        }

        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<RevenueReportResponse>(correlationId);
        }

        var documents = await _reports.AggregateDocumentRevenueAsync(
            context.TenantId, window.FromUtc, window.ToUtc, cancellationToken);

        var overage = await _reports.AggregateOverageRevenueAsync(
            context.TenantId, window.FromUtc, window.ToUtc, cancellationToken);

        var currencyCodes = documents
            .Select(bucket => bucket.CurrencyCode)
            .Concat(overage.Select(bucket => bucket.CurrencyCode))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal);

        var currencies = new List<RevenueCurrencyResponse>();

        foreach (var currencyCode in currencyCodes)
        {
            var forCurrency = documents
                .Where(bucket => string.Equals(
                    bucket.CurrencyCode, currencyCode, StringComparison.Ordinal))
                .ToList();

            var subscriptionRevenue = forCurrency
                .Where(bucket => bucket.DocumentType != FinancialDocumentType.CreditNote)
                .Sum(bucket => bucket.TotalMinor);

            var subscriptionDocuments = forCurrency
                .Where(bucket => bucket.DocumentType != FinancialDocumentType.CreditNote)
                .Sum(bucket => bucket.DocumentCount);

            var creditNotes = forCurrency
                .Where(bucket => bucket.DocumentType == FinancialDocumentType.CreditNote)
                .Sum(bucket => bucket.TotalMinor);

            var overageForCurrency = overage
                .Where(bucket => string.Equals(
                    bucket.CurrencyCode, currencyCode, StringComparison.Ordinal))
                .ToList();

            var overageRevenue = overageForCurrency.Sum(bucket => bucket.TotalMinor);

            currencies.Add(new RevenueCurrencyResponse
            {
                CurrencyCode = currencyCode,
                SubscriptionRevenueMinor = subscriptionRevenue,
                OverageRevenueMinor = overageRevenue,
                TotalRevenueMinor = subscriptionRevenue + overageRevenue,
                CreditNoteMinor = Math.Abs(creditNotes),
                SubscriptionDocumentCount = subscriptionDocuments,
                OverageInvoiceCount = overageForCurrency.Sum(bucket => bucket.InvoiceCount)
            });
        }

        return SubscriptionOperationResult<RevenueReportResponse>.Success(
            new RevenueReportResponse
            {
                FromUtc = window.FromUtc,
                ToUtc = window.ToUtc,
                Currencies = currencies
            },
            correlationId);
    }

    public async Task<SubscriptionOperationResult<DunningReportResponse>> GetDunningAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<DunningReportResponse>(correlationId);
        }

        var subscriptions = await _reports.ListByStatusAsync(
            context.TenantId, DunningStatuses, cancellationToken);

        var invoices = await _reports.ListRetryingUsageInvoicesAsync(
            context.TenantId, MaximumDunningInvoices, cancellationToken);

        return SubscriptionOperationResult<DunningReportResponse>.Success(
            new DunningReportResponse
            {
                Subscriptions = [.. subscriptions
                    .OrderBy(subscription => subscription.PastDueSinceUtc ?? DateTime.MaxValue)
                    .Select(subscription => new DunningSubscriptionResponse
                    {
                        SubscriptionId = subscription.ItemId,
                        OrganizationId = subscription.OrganizationId,
                        Status = subscription.Status.ToString(),
                        PlanCode = subscription.Plan.Code,
                        CurrencyCode = subscription.CurrencyCode,
                        PeriodAmountMinor = SubscriptionAmountCalculator.GrossAmountMinor(
                            subscription.Price,
                            subscription.QuantityItems),
                        PastDueSinceUtc = subscription.PastDueSinceUtc,
                        DunningAttemptCount = subscription.DunningAttemptCount,
                        NextFeeBillingAtUtc = subscription.NextFeeBillingAtUtc,
                        CurrentPeriodEndUtc = subscription.CurrentPeriodEndUtc
                    })],
                UsageInvoices = [.. invoices.Select(invoice => new DunningUsageInvoiceResponse
                {
                    UsageInvoiceId = invoice.ItemId,
                    SubscriptionId = invoice.SubscriptionId,
                    OrganizationId = invoice.OrganizationId,
                    PeriodKey = invoice.PeriodKey,
                    CurrencyCode = invoice.CurrencyCode,
                    TotalAmountMinor = invoice.TotalAmountMinor,
                    AttemptCount = invoice.AttemptCount,
                    NextAttemptAtUtc = invoice.NextAttemptAtUtc,
                    LastError = invoice.LastError,
                    CreatedAtUtc = invoice.CreatedAtUtc
                })],
                SubscriptionCount = subscriptions.Count,
                UsageInvoiceCount = invoices.Count
            },
            correlationId);
    }

    public async Task<SubscriptionOperationResult<SubscriptionRosterReportResponse>> GetRosterAsync(
        GetSubscriptionReportRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.PageSize is < 1 or > MaximumPageSize)
        {
            return Invalid<SubscriptionRosterReportResponse>(
                correlationId,
                nameof(request.PageSize),
                $"PageSize must be between 1 and {MaximumPageSize}.");
        }

        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<SubscriptionRosterReportResponse>(correlationId);
        }

        SubscriptionReportCursor? after = null;

        if (request.After is not null &&
            !SubscriptionReportCursorCodec.TryDecode(request.After, context.TenantId, out after))
        {
            return Invalid<SubscriptionRosterReportResponse>(
                correlationId,
                nameof(request.After),
                "After is not a valid cursor.");
        }

        var page = await _reports.ListSubscriptionsAsync(
            context.TenantId, request.PageSize, after, cancellationToken);

        var usage = await _reports.ListCurrentUsageAsync(
            context.TenantId,
            [.. page.Items.Select(subscription => subscription.ItemId)],
            cancellationToken);

        var usageBySubscription = usage
            .GroupBy(current => current.SubscriptionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var last = page.Items.Count > 0
            ? page.Items[page.Items.Count - 1]
            : null;

        return SubscriptionOperationResult<SubscriptionRosterReportResponse>.Success(
            new SubscriptionRosterReportResponse
            {
                Items = [.. page.Items.Select(
                    subscription => MapRosterRow(subscription, usageBySubscription, nowUtc))],
                PageInfo = new SubscriptionReportPageInfoResponse
                {
                    PageSize = request.PageSize,
                    HasNextPage = page.HasMore,
                    NextCursor = page.HasMore && last is not null
                        ? SubscriptionReportCursorCodec.Encode(
                            context.TenantId,
                            new SubscriptionReportCursor(last.CreatedAtUtc, last.ItemId))
                        : null
                }
            },
            correlationId);
    }

    public async Task<SubscriptionOperationResult<CouponReportResponse>> GetCouponsAsync(
        GetRevenueReportRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryResolveWindow(
                request.FromUtc,
                request.ToUtc,
                correlationId,
                nameof(request.FromUtc),
                out var window,
                out var windowFailure))
        {
            return windowFailure!.ToFailure<CouponReportResponse>();
        }

        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<CouponReportResponse>(correlationId);
        }

        var uptake = await _reports.AggregateCouponUptakeAsync(
            context.TenantId, window.FromUtc, window.ToUtc, cancellationToken);

        var revenue = await _reports.AggregateCouponRevenueAsync(
            context.TenantId, window.FromUtc, window.ToUtc, cancellationToken);

        var redemptions = await _reports.AggregateCampaignRedemptionsAsync(
            context.TenantId, cancellationToken);

        var codesById = await _reports.GetDiscountCodesAsync(
            context.TenantId,
            [.. redemptions.Select(bucket => bucket.DiscountId).Distinct(StringComparer.Ordinal)],
            cancellationToken);

        // Redemptions are keyed by discount id; everything else is keyed by code. A redemption
        // whose discount has since been deleted keeps its id as the key rather than being dropped,
        // so a campaign that was removed still accounts for the subscriptions it created.
        var redemptionsByCode = redemptions
            .GroupBy(
                bucket => codesById.TryGetValue(bucket.DiscountId, out var code)
                    ? code
                    : bucket.DiscountId,
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var codes = uptake
            .Select(bucket => bucket.Code)
            .Concat(revenue.Select(bucket => bucket.Code))
            .Concat(redemptionsByCode.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal);

        var rows = new List<CouponReportRowResponse>();

        foreach (var code in codes)
        {
            var uptakeForCode = uptake
                .Where(bucket => string.Equals(bucket.Code, code, StringComparison.Ordinal))
                .ToList();

            var revenueForCode = revenue
                .Where(bucket => string.Equals(bucket.Code, code, StringComparison.Ordinal))
                .ToList();

            rows.Add(new CouponReportRowResponse
            {
                Code = code,
                RedeemingOrganizations = uptakeForCode
                    .Select(bucket => bucket.OrganizationId)
                    .Distinct(StringComparer.Ordinal)
                    .LongCount(),
                SubscriptionCount = uptakeForCode.Sum(bucket => bucket.SubscriptionCount),
                CampaignStates = redemptionsByCode.TryGetValue(code, out var states)
                    ? [.. states
                        .GroupBy(bucket => bucket.State)
                        .OrderBy(group => group.Key)
                        .Select(group => new CouponRedemptionStateResponse
                        {
                            State = group.Key.ToString(),
                            Count = group.Sum(bucket => bucket.Count)
                        })]
                    : [],
                Currencies = [.. revenueForCode
                    .GroupBy(bucket => bucket.CurrencyCode, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new CouponCurrencyResponse
                    {
                        CurrencyCode = group.Key,
                        RevenueMinor = group.Sum(bucket => bucket.RevenueMinor),
                        DiscountGivenMinor = group.Sum(bucket => bucket.DiscountGivenMinor),
                        DocumentCount = group.Sum(bucket => bucket.DocumentCount)
                    })]
            });
        }

        return SubscriptionOperationResult<CouponReportResponse>.Success(
            new CouponReportResponse
            {
                FromUtc = window.FromUtc,
                ToUtc = window.ToUtc,
                Coupons = rows
            },
            correlationId);
    }

    private static IReadOnlyList<UsageReportBucketResponse> MapUsageBuckets(
        IReadOnlyList<UsageLedgerBucket> buckets) =>
        [.. buckets
            .GroupBy(bucket => new { bucket.MeterKey, bucket.BucketStartUtc })
            .OrderBy(group => group.Key.BucketStartUtc)
            .ThenBy(group => group.Key.MeterKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var consumed = QuantityOf(group, UsageEntryType.Consumption);

                // Reversals are stored with a negative delta, so this is already signed the way
                // the net needs it and is flipped only for reporting the gross reversal.
                var reversed = QuantityOf(group, UsageEntryType.Reversal);

                return new UsageReportBucketResponse
                {
                    MeterKey = group.Key.MeterKey,
                    BucketStartUtc = group.Key.BucketStartUtc,
                    ConsumedQuantity = consumed,
                    ReversedQuantity = Math.Abs(reversed),
                    NetQuantity = consumed + reversed,
                    GrantedQuantity = QuantityOf(group, UsageEntryType.Grant),
                    RecordCount = group.Sum(bucket => bucket.RecordCount)
                };
            })];

    private static decimal QuantityOf(
        IEnumerable<UsageLedgerBucket> buckets,
        UsageEntryType entryType) =>
        buckets
            .Where(bucket => bucket.EntryType == entryType)
            .Sum(bucket => bucket.Quantity);

    private static SubscriptionRunRateEntry RunRateOf(
        SubscriptionDetail subscription,
        DateTime nowUtc)
    {
        var grossPeriodMinor = SubscriptionAmountCalculator.GrossAmountMinor(
            subscription.Price,
            subscription.QuantityItems);

        // The same expression renewal charges through, so a reported run-rate and the next
        // invoice cannot disagree about whether a discount still applies.
        var netPeriod = SubscriptionAmountCalculator.ApplyDiscount(
            grossPeriodMinor,
            subscription.Discount,
            subscription.DiscountPeriodsApplied,
            nowUtc);

        return new SubscriptionRunRateEntry(
            subscription.CurrencyCode,
            SubscriptionRunRate.BillableSeats(subscription.Price, subscription.QuantityItems),
            SubscriptionRunRate.MonthlyAmountMinor(
                grossPeriodMinor,
                subscription.Price.Interval,
                subscription.Price.IntervalCount),
            SubscriptionRunRate.MonthlyAmountMinor(
                netPeriod.AmountMinor,
                subscription.Price.Interval,
                subscription.Price.IntervalCount));
    }

    private static IReadOnlyList<RecurringRevenueTierResponse> MapTiers(
        IEnumerable<SubscriptionRunRateEntry> entries)
    {
        var byTier = entries
            .GroupBy(entry => SubscriptionRunRate.TierFor(entry.Seats).Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        // Every band is emitted, including the empty ones. A tier missing from the response is
        // indistinguishable from a tier the report forgot, and a chart with a hole in it invites
        // exactly the wrong conclusion.
        return [.. SubscriptionRunRate.Tiers.Select(tier =>
        {
            var members = byTier.TryGetValue(tier.Name, out var found)
                ? found
                : [];

            var grossMonthly = members.Sum(entry => entry.GrossMonthlyMinor);
            var netMonthly = members.Sum(entry => entry.NetMonthlyMinor);

            return new RecurringRevenueTierResponse
            {
                Tier = tier.Name,
                MinimumSeats = tier.MinimumSeats,
                MaximumSeats = tier.MaximumSeats,
                SubscriptionCount = members.Count,
                SeatCount = members.Sum(entry => entry.Seats),
                GrossMonthlyMinor = grossMonthly,
                NetMonthlyMinor = netMonthly,
                GrossAnnualMinor = SubscriptionRunRate.AnnualFromMonthly(grossMonthly),
                NetAnnualMinor = SubscriptionRunRate.AnnualFromMonthly(netMonthly)
            };
        })];
    }

    private static SubscriptionRosterRowResponse MapRosterRow(
        SubscriptionDetail subscription,
        Dictionary<string, List<SubscriptionUsageCurrent>> usageBySubscription,
        DateTime nowUtc)
    {
        var runRate = RunRateOf(subscription, nowUtc);

        return new SubscriptionRosterRowResponse
        {
            SubscriptionId = subscription.ItemId,
            OrganizationId = subscription.OrganizationId,
            Status = subscription.Status.ToString(),
            PlanCode = subscription.Plan.Code,
            PlanName = subscription.Plan.DisplayName,
            SeatTier = SubscriptionRunRate.TierFor(runRate.Seats).Name,
            SeatCount = runRate.Seats,
            CurrencyCode = subscription.CurrencyCode,
            GrossMonthlyMinor = runRate.GrossMonthlyMinor,
            NetMonthlyMinor = runRate.NetMonthlyMinor,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            CurrentPeriodEndUtc = subscription.CurrentPeriodEndUtc,
            CreatedAtUtc = subscription.CreatedAtUtc,
            Meters = usageBySubscription.TryGetValue(subscription.ItemId, out var meters)
                ? [.. meters
                    .OrderBy(current => current.MeterKey, StringComparer.Ordinal)
                    .Select(MapMeter)]
                : []
        };
    }

    private static SubscriptionRosterMeterResponse MapMeter(SubscriptionUsageCurrent current) =>
        new()
        {
            MeterKey = current.MeterKey,
            PeriodKey = current.PeriodKey,
            Included = current.Included,
            Used = current.Used,
            Remaining = current.Remaining,
            Overage = current.Overage,
            // Null rather than zero when there is no allowance to be a percentage of. A zero here
            // reads as "nothing used", which is the opposite of what an unlimited meter means.
            UsedPercentOfQuota = current.Included > 0
                ? Math.Round(current.Used * 100m / current.Included, 2)
                : null,
            OverageAllowed = current.OverageAllowed,
            UpdatedAtUtc = current.UpdatedAtUtc
        };

    /// <summary>
    /// Settles the window a report covers, defaulting to the current UTC month.
    /// </summary>
    /// <remarks>
    /// <paramref name="toUtc"/> is exclusive throughout. An inclusive upper bound on a timestamp
    /// either drops everything recorded in the final second or double-counts a boundary between
    /// two adjacent reports, depending on which mistake is made — an exclusive one has neither
    /// problem and makes consecutive windows tile exactly.
    /// </remarks>
    private bool TryResolveWindow(
        DateTime? fromUtc,
        DateTime? toUtc,
        string correlationId,
        string field,
        out (DateTime FromUtc, DateTime ToUtc) window,
        out SubscriptionOperationResult<bool>? failure)
    {
        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var from = (fromUtc ?? monthStart).ToUniversalTime();
        var to = (toUtc ?? monthStart.AddMonths(1)).ToUniversalTime();

        window = (from, to);
        failure = null;

        if (from >= to)
        {
            failure = Invalid<bool>(
                correlationId, field, "FromUtc must be earlier than ToUtc.");

            return false;
        }

        if ((to - from).TotalDays > MaximumRangeDays)
        {
            failure = Invalid<bool>(
                correlationId,
                field,
                $"The reporting window must not exceed {MaximumRangeDays} days.");

            return false;
        }

        return true;
    }

    private static SubscriptionOperationResult<TValue> Invalid<TValue>(
        string correlationId,
        string field,
        string message) =>
        SubscriptionOperationResult<TValue>.Failure(
            PaymentFailureKind.Validation,
            "subscription_report_query_invalid",
            "The report query is invalid.",
            correlationId,
            new Dictionary<string, string[]> { [field] = [message] });

    private sealed record SubscriptionRunRateEntry(
        string CurrencyCode,
        long Seats,
        long GrossMonthlyMinor,
        long NetMonthlyMinor);
}
