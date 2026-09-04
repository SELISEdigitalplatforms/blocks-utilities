using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <inheritdoc cref="ISubscriptionUsageReportService" />
public sealed class SubscriptionUsageReportService : ISubscriptionUsageReportService
{
    private const int MaximumPageSize = 100;
    private const int DefaultPageSize = 25;

    private readonly ISubscriptionContextResolver _context;
    private readonly ISubscriptionUsageActivityRollupRepository _activity;
    private readonly ISubscriptionUsageActorRollupRepository _actors;
    private readonly ISubscriptionUsageInvoiceRepository _invoices;
    private readonly ISubscriptionUsageCurrentRepository _current;
    private readonly TimeProvider _time;

    public SubscriptionUsageReportService(
        ISubscriptionContextResolver context,
        ISubscriptionUsageActivityRollupRepository activity,
        ISubscriptionUsageActorRollupRepository actors,
        ISubscriptionUsageInvoiceRepository invoices,
        ISubscriptionUsageCurrentRepository current,
        TimeProvider? time = null)
    {
        _context = context;
        _activity = activity;
        _actors = actors;
        _invoices = invoices;
        _current = current;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<UsageTimeseriesResponse>> GetTimeseriesAsync(
        GetUsageReportRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidateCommon<UsageTimeseriesResponse>(
                request, correlationId, out var pageSize, out var failure))
        {
            return failure!;
        }

        if (!TryParseGranularity(request.Granularity, out var granularity))
        {
            return Invalid<UsageTimeseriesResponse>(
                correlationId, nameof(request.Granularity),
                "Granularity must be Day, Week, Month or Year.");
        }

        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<UsageTimeseriesResponse>(correlationId);
        }

        var scope = ScopeFor(context.TenantId, request, granularity.ToString());

        DateTime? after = null;
        if (request.After is not null)
        {
            if (!UsageReportCursorCodec.TryDecode(request.After, scope, out var boundary) ||
                !DateTime.TryParse(
                    boundary,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var parsed))
            {
                return Invalid<UsageTimeseriesResponse>(
                    correlationId, nameof(request.After), "After is not a valid cursor.");
            }

            after = parsed;
        }

        var page = await _activity.SumByPeriodAsync(
            context.TenantId,
            request.OrganizationId,
            request.SubscriptionId,
            request.MeterKey,
            request.FromUtc?.ToUniversalTime(),
            request.ToUtc?.ToUniversalTime(),
            granularity,
            pageSize,
            after,
            cancellationToken);

        var last = page.Items.LastOrDefault();

        return SubscriptionOperationResult<UsageTimeseriesResponse>.Success(
            new UsageTimeseriesResponse
            {
                Items = [.. page.Items.Select(item => new UsageTimeseriesPointResponse
                {
                    PeriodKey = PeriodKey.Create(granularity, item.PeriodStartUtc),
                    PeriodStartUtc = item.PeriodStartUtc,
                    ConsumedQuantity = item.ConsumedQuantity,
                    EntryCount = item.EntryCount
                })],
                PageInfo = PageInfo(
                    pageSize,
                    page.HasMore,
                    last is not null
                        ? UsageReportCursorCodec.Encode(scope, last.PeriodStartUtc.ToString("O"))
                        : null)
            },
            correlationId);
    }

    public async Task<SubscriptionOperationResult<UsageOrganizationBreakdownResponse>>
        GetOrganizationsAsync(
            GetUsageReportRequest request,
            string correlationId,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidateCommon<UsageOrganizationBreakdownResponse>(
                request, correlationId, out var pageSize, out var failure))
        {
            return failure!;
        }

        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<UsageOrganizationBreakdownResponse>(correlationId);
        }

        var scope = ScopeFor(context.TenantId, request, granularity: null);

        UsageOrganizationTotalsCursor? after = null;
        if (request.After is not null)
        {
            if (!UsageReportCursorCodec.TryDecode(request.After, scope, out var boundary) ||
                !TryDecodeOrganizationCursor(boundary, out after))
            {
                return Invalid<UsageOrganizationBreakdownResponse>(
                    correlationId, nameof(request.After), "After is not a valid cursor.");
            }
        }

        var page = await _activity.SumByOrganizationAsync(
            context.TenantId,
            request.SubscriptionId,
            request.MeterKey,
            request.FromUtc?.ToUniversalTime(),
            request.ToUtc?.ToUniversalTime(),
            pageSize,
            after,
            cancellationToken);

        var last = page.Items.LastOrDefault();

        return SubscriptionOperationResult<UsageOrganizationBreakdownResponse>.Success(
            new UsageOrganizationBreakdownResponse
            {
                Items = [.. page.Items.Select(item => new UsageOrganizationTotalResponse
                {
                    OrganizationId = item.OrganizationId,
                    ConsumedQuantity = item.ConsumedQuantity,
                    EntryCount = item.EntryCount
                })],
                PageInfo = PageInfo(
                    pageSize,
                    page.HasMore,
                    last is not null
                        ? UsageReportCursorCodec.Encode(
                            scope, $"{last.ConsumedQuantity:G29}|{last.OrganizationId}")
                        : null)
            },
            correlationId);
    }

    public async Task<SubscriptionOperationResult<UsageActorBreakdownResponse>> GetActorsAsync(
        GetUsageReportRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidateCommon<UsageActorBreakdownResponse>(
                request, correlationId, out var pageSize, out var failure))
        {
            return failure!;
        }

        // Per-actor totals are asked within one organization: a user is a member of an
        // organization, not of a tenant at large, so "every user on the tenant" is not a
        // meaningful single list the way "every organization" or "every day" is.
        if (string.IsNullOrWhiteSpace(request.OrganizationId))
        {
            return Invalid<UsageActorBreakdownResponse>(
                correlationId, nameof(request.OrganizationId),
                "OrganizationId is required for the actor breakdown.");
        }

        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<UsageActorBreakdownResponse>(correlationId);
        }

        var scope = ScopeFor(context.TenantId, request, granularity: null);

        UsageActorRollupCursor? after = null;
        if (request.After is not null)
        {
            if (!UsageReportCursorCodec.TryDecode(request.After, scope, out var boundary) ||
                !TryDecodeActorCursor(boundary, out after))
            {
                return Invalid<UsageActorBreakdownResponse>(
                    correlationId, nameof(request.After), "After is not a valid cursor.");
            }
        }

        var page = await _actors.ListAsync(
            context.TenantId,
            request.OrganizationId!,
            request.MeterKey,
            request.FromUtc?.ToUniversalTime(),
            request.ToUtc?.ToUniversalTime(),
            pageSize,
            after,
            cancellationToken);

        var last = page.Items.LastOrDefault();

        return SubscriptionOperationResult<UsageActorBreakdownResponse>.Success(
            new UsageActorBreakdownResponse
            {
                Items = [.. page.Items.Select(item => new UsageActorTotalResponse
                {
                    OrganizationId = item.OrganizationId,
                    MeterKey = item.MeterKey,
                    DayUtc = item.DayUtc,
                    UserId = item.UserId,
                    ConsumedQuantity = item.ConsumedQuantity,
                    EntryCount = item.EntryCount
                })],
                PageInfo = PageInfo(
                    pageSize,
                    page.HasMore,
                    last is not null
                        ? UsageReportCursorCodec.Encode(
                            scope, $"{last.DayUtc:O}|{last.UserId}")
                        : null)
            },
            correlationId);
    }

    public async Task<SubscriptionOperationResult<UsageAllowanceHistoryResponse>> GetAllowancesAsync(
        GetUsageReportRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidateCommon<UsageAllowanceHistoryResponse>(
                request, correlationId, out var pageSize, out var failure))
        {
            return failure!;
        }

        if (string.IsNullOrWhiteSpace(request.OrganizationId))
        {
            return Invalid<UsageAllowanceHistoryResponse>(
                correlationId, nameof(request.OrganizationId),
                "OrganizationId is required for the allowance history.");
        }

        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<UsageAllowanceHistoryResponse>(correlationId);
        }

        var scope = ScopeFor(context.TenantId, request, granularity: null);

        UsageInvoiceCursor? after = null;
        if (request.After is not null)
        {
            if (!UsageReportCursorCodec.TryDecode(request.After, scope, out var boundary) ||
                !TryDecodeInvoiceCursor(boundary, out after))
            {
                return Invalid<UsageAllowanceHistoryResponse>(
                    correlationId, nameof(request.After), "After is not a valid cursor.");
            }
        }

        var closedPage = await _invoices.ListAsync(
            context.TenantId,
            request.OrganizationId,
            request.SubscriptionId,
            request.FromUtc?.ToUniversalTime(),
            request.ToUtc?.ToUniversalTime(),
            pageSize,
            after,
            cancellationToken);

        var items = new List<UsageAllowancePeriodResponse>();

        foreach (var invoice in closedPage.Items)
        {
            var meters = invoice.Lines
                .Where(line => request.MeterKey is null ||
                               string.Equals(line.MeterKey, request.MeterKey, StringComparison.Ordinal))
                .Select(line => MapClosedMeter(line))
                .ToList();

            if (meters.Count == 0)
            {
                continue;
            }

            items.Add(new UsageAllowancePeriodResponse
            {
                OrganizationId = invoice.OrganizationId,
                SubscriptionId = invoice.SubscriptionId,
                PeriodKey = invoice.PeriodKey,
                IsOpenPeriod = false,
                Meters = meters
            });
        }

        // The open period is appended only on the first page: it has no place in a keyset walk
        // over closed invoices, and repeating it on every page would misrepresent it as recurring
        // history rather than the one window still in progress.
        if (after is null)
        {
            var open = await _current.ListByOrganizationAsync(
                context.TenantId,
                request.OrganizationId,
                request.SubscriptionId,
                request.MeterKey,
                _time.GetUtcNow().UtcDateTime,
                limit: 200,
                cancellationToken);

            foreach (var bySubscription in open.GroupBy(
                         document => document.SubscriptionId, StringComparer.Ordinal))
            {
                items.Add(new UsageAllowancePeriodResponse
                {
                    OrganizationId = request.OrganizationId,
                    SubscriptionId = bySubscription.Key,
                    PeriodKey = bySubscription.First().PeriodKey,
                    IsOpenPeriod = true,
                    Meters = [.. bySubscription.Select(document => new UsageAllowanceMeterResponse
                    {
                        MeterKey = document.MeterKey,
                        PlanId = document.PlanId,
                        PlanCode = document.PlanCode,
                        IncludedQuantity = document.Included,
                        UsedQuantity = document.Used,
                        OverageQuantity = document.Overage,
                        OverageAmountMinor = null,
                        IsHistoricalOverageOnly = false
                    })]
                });
            }
        }

        var lastClosed = closedPage.Items.LastOrDefault();

        return SubscriptionOperationResult<UsageAllowanceHistoryResponse>.Success(
            new UsageAllowanceHistoryResponse
            {
                Items = items,
                PageInfo = PageInfo(
                    pageSize,
                    closedPage.HasMore,
                    lastClosed is not null
                        ? UsageReportCursorCodec.Encode(
                            scope, $"{lastClosed.CreatedAtUtc:O}|{lastClosed.ItemId}")
                        : null)
            },
            correlationId);
    }

    private static UsageAllowanceMeterResponse MapClosedMeter(UsageInvoiceLine line)
    {
        // A period rated before this feature's rating fix shipped carries only OverageQuantity —
        // IncludedQuantity and UsedQuantity deserialize as zero because the field did not exist
        // yet. The combination of "overage present" with "nothing used" is otherwise impossible
        // (overage can never exceed usage), so it is the signal that this line predates the fix
        // rather than a real zero-allowance, zero-usage period.
        var isHistorical = line.UsedQuantity == 0 && line.OverageQuantity > 0;

        return new UsageAllowanceMeterResponse
        {
            MeterKey = line.MeterKey,
            IncludedQuantity = line.IncludedQuantity,
            UsedQuantity = line.UsedQuantity,
            OverageQuantity = line.OverageQuantity,
            OverageAmountMinor = line.AmountMinor,
            IsHistoricalOverageOnly = isHistorical
        };
    }

    private static UsageReportCursorScope ScopeFor(
        string tenantId, GetUsageReportRequest request, string? granularity) =>
        new(
            tenantId,
            request.OrganizationId,
            request.SubscriptionId,
            request.MeterKey,
            granularity,
            request.FromUtc?.ToUniversalTime(),
            request.ToUtc?.ToUniversalTime());

    private static bool TryValidateCommon<TValue>(
        GetUsageReportRequest request,
        string correlationId,
        out int pageSize,
        out SubscriptionOperationResult<TValue>? failure)
    {
        pageSize = request.PageSize <= 0 ? DefaultPageSize : request.PageSize;
        failure = null;

        if (pageSize is < 1 or > MaximumPageSize)
        {
            failure = Invalid<TValue>(
                correlationId, nameof(request.PageSize),
                $"PageSize must be between 1 and {MaximumPageSize}.");

            return false;
        }

        if (request.FromUtc is { } from && request.ToUtc is { } to && from > to)
        {
            failure = Invalid<TValue>(
                correlationId, nameof(request.FromUtc),
                "FromUtc must not be later than ToUtc.");

            return false;
        }

        return true;
    }

    /// <summary>
    /// Parsed by hand so an unrecognised value is a domain error rather than a silent fallback to
    /// the default — mirroring <c>SubscriptionPlansController.TryParseCatalogueFilter</c>.
    /// </summary>
    private static bool TryParseGranularity(string? value, out BillingInterval granularity)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            granularity = BillingInterval.Month;

            return true;
        }

        switch (value.Trim())
        {
            case var day when day.Equals(nameof(BillingInterval.Day), StringComparison.OrdinalIgnoreCase):
                granularity = BillingInterval.Day;

                return true;
            case var week when week.Equals(
                nameof(BillingInterval.Week), StringComparison.OrdinalIgnoreCase):
                granularity = BillingInterval.Week;

                return true;
            case var month when month.Equals(
                nameof(BillingInterval.Month), StringComparison.OrdinalIgnoreCase):
                granularity = BillingInterval.Month;

                return true;
            case var year when year.Equals(
                nameof(BillingInterval.Year), StringComparison.OrdinalIgnoreCase):
                granularity = BillingInterval.Year;

                return true;
            default:
                granularity = default;

                return false;
        }
    }

    private static bool TryDecodeOrganizationCursor(
        string boundary, out UsageOrganizationTotalsCursor? cursor)
    {
        cursor = null;
        var parts = boundary.Split('|', 2);

        if (parts.Length != 2 ||
            !decimal.TryParse(
                parts[0], System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var quantity) ||
            string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        cursor = new UsageOrganizationTotalsCursor(quantity, parts[1]);

        return true;
    }

    private static bool TryDecodeActorCursor(string boundary, out UsageActorRollupCursor? cursor)
    {
        cursor = null;
        var parts = boundary.Split('|', 2);

        if (parts.Length != 2 ||
            !DateTime.TryParse(
                parts[0], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var day) ||
            string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        cursor = new UsageActorRollupCursor(day, parts[1]);

        return true;
    }

    private static bool TryDecodeInvoiceCursor(string boundary, out UsageInvoiceCursor? cursor)
    {
        cursor = null;
        var parts = boundary.Split('|', 2);

        if (parts.Length != 2 ||
            !DateTime.TryParse(
                parts[0], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var createdAt) ||
            string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        cursor = new UsageInvoiceCursor(createdAt, parts[1]);

        return true;
    }

    private static UsageReportPageInfoResponse PageInfo(
        int pageSize, bool hasMore, string? nextCursor) =>
        new()
        {
            PageSize = pageSize,
            HasNextPage = hasMore,
            NextCursor = hasMore ? nextCursor : null
        };

    private static SubscriptionOperationResult<TValue> Invalid<TValue>(
        string correlationId, string field, string message) =>
        SubscriptionOperationResult<TValue>.Failure(
            PaymentFailureKind.Validation,
            "subscription_usage_report_query_invalid",
            "The usage report query is invalid.",
            correlationId,
            new Dictionary<string, string[]> { [field] = [message] });
}
