using Api.Utilities;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Subscription.DomainService.Reporting;

namespace Api.Controllers;

/// <summary>
/// Reporting over the subscription book. Served under <c>/api/subscription-reports</c>.
/// </summary>
/// <remarks>
/// Every endpoint answers for the authenticated caller's own tenant, across all of its
/// organizations — which is the question an operator is asking. That is wider than every other
/// subscription endpoint, all of which answer for one organization, so the scope on these actions
/// is the only thing standing between a caller and the whole tenant's commercial position. It is
/// deliberately a separate scope from <c>subscription::read</c> rather than a reuse of it: a
/// client permitted to read its own subscription is not thereby permitted to read everyone's.
/// <para>
/// All of it is read-only and computed at request time from the live collections, so a figure is
/// never stale and there is no rollup to rebuild. The cost is that a wide window is a wide scan,
/// which is why the service refuses one longer than a year.
/// </para>
/// <para>
/// Money is reported grouped by currency and never summed across currencies. This module holds no
/// exchange rates and a subscription's currency is fixed for its life, so a single total would be
/// an addition of unlike things.
/// </para>
/// </remarks>
[ApiController]
[Route("subscription-reports")]
public sealed class SubscriptionReportsController : ControllerBase
{
    private readonly ISubscriptionReportingService _reports;

    public SubscriptionReportsController(ISubscriptionReportingService reports) =>
        _reports = reports;

    /// <summary>
    /// Metered volume over a window, bucketed by day or month.
    /// </summary>
    /// <remarks>
    /// <c>meterKey</c> selects one meter and defaults to all of them. It is a parameter because a
    /// meter key is the tenant's own word — this endpoint has no idea which meter is the
    /// interesting one, and is built so it never needs to.
    /// <para>
    /// Comparing two months is one call for a two-month window at <c>granularity=month</c>; today
    /// is one call for a one-day window. There is no separate comparison endpoint because there is
    /// no separate question.
    /// </para>
    /// <para>
    /// Four figures come back per bucket, not one. Consumption and reversals are reported apart so
    /// a low month can be told from a corrected one, and grants are excluded from the net because
    /// a grant raises an allowance rather than consuming it.
    /// </para>
    /// </remarks>
    [HttpGet("usage")]
    [ProducesResponseType(typeof(ApiResponse<UsageReportResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UsageReportResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint("blocks-utilities::subscription-report::read")]
    public async Task<IActionResult> GetUsage(
        [FromQuery] GetUsageReportRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _reports.GetUsageAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Monthly and annual run-rate, by currency and seat tier.
    /// </summary>
    /// <remarks>
    /// Computed from the live subscriptions themselves rather than from past invoices, so it is a
    /// forward-looking rate and not a record of what was billed — a subscription that signed up
    /// yesterday counts in full. <c>GET /revenue</c> is the endpoint for what was actually
    /// collected.
    /// <para>
    /// Gross is list price; net is after any promotional discount still being honoured, decided by
    /// the same expression renewal charges through. The gap between them is what the live
    /// discounts cost.
    /// </para>
    /// </remarks>
    [HttpGet("recurring-revenue")]
    [ProducesResponseType(
        typeof(ApiResponse<RecurringRevenueReportResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint("blocks-utilities::subscription-report::read")]
    public async Task<IActionResult> GetRecurringRevenue(CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _reports.GetRecurringRevenueAsync(correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Billed revenue over a window, split between the subscription fee and metered overage.
    /// </summary>
    /// <remarks>
    /// The split is exact rather than inferred: recurring charges produce financial documents and
    /// overage produces usage invoices, so the two are counted from different collections and no
    /// line item has to be interpreted.
    /// <para>
    /// Credit notes are reported beside the revenue rather than netted out of it. One commonly
    /// refunds a charge from an earlier window, and subtracting it here would reduce a month that
    /// never earned it.
    /// </para>
    /// </remarks>
    [HttpGet("revenue")]
    [ProducesResponseType(typeof(ApiResponse<RevenueReportResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<RevenueReportResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint("blocks-utilities::subscription-report::read")]
    public async Task<IActionResult> GetRevenue(
        [FromQuery] GetRevenueReportRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _reports.GetRevenueAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// What is failing to collect: subscriptions in their dunning cycle and usage invoices still
    /// being retried.
    /// </summary>
    /// <remarks>
    /// Named for dunning rather than for overdue invoices, because this module has no accounts
    /// receivable to be overdue against. A financial document is issued only after a charge has
    /// settled and has no unpaid state, so an invoice here is a receipt. What is genuinely
    /// outstanding is the two lists this returns.
    /// <para>
    /// The invoice list is capped; the count beside it is not, so the true size of a provider
    /// outage is still visible when the list has been truncated.
    /// </para>
    /// </remarks>
    [HttpGet("dunning")]
    [ProducesResponseType(typeof(ApiResponse<DunningReportResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint("blocks-utilities::subscription-report::read")]
    public async Task<IActionResult> GetDunning(CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _reports.GetDunningAsync(correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Every subscription in the tenant with its plan, seat tier and usage against quota.
    /// </summary>
    /// <remarks>
    /// Usage comes from the published current-usage projection, which can lag the counters or have
    /// published nothing yet. A subscription with no published row returns an empty meter list
    /// rather than zeroed figures, and every meter carries the instant it was last published, so
    /// staleness is visible instead of assumed. This is a report and never an entitlement check:
    /// only <c>POST /api/subscription-usage</c> decides whether a unit may be consumed.
    /// <para>
    /// Paged by opaque cursor rather than by offset, so a subscription created mid-page cannot
    /// shift a later page and hide a row. A cursor is bound to the tenant it was issued for and is
    /// refused anywhere else.
    /// </para>
    /// </remarks>
    [HttpGet("subscriptions")]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionRosterReportResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionRosterReportResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint("blocks-utilities::subscription-report::read")]
    public async Task<IActionResult> GetSubscriptions(
        [FromQuery] GetSubscriptionReportRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _reports.GetRosterAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Coupon uptake and what each code earned and gave away.
    /// </summary>
    /// <remarks>
    /// Uptake is counted from the discount recorded on subscriptions, not from the campaign
    /// redemption ledger. Redemptions are written only for campaign-kind discounts, so a count
    /// taken from there would report zero for every ordinary promotional code — a wrong answer
    /// indistinguishable from a real one. Campaign state is attached where it exists and is absent
    /// for an ordinary code, which is a real distinction and not missing data.
    /// <para>
    /// Counts are of organizations, which is what every record involved is keyed to. Revenue and
    /// discount are grouped by currency.
    /// </para>
    /// </remarks>
    [HttpGet("coupons")]
    [ProducesResponseType(typeof(ApiResponse<CouponReportResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<CouponReportResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint("blocks-utilities::subscription-report::read")]
    public async Task<IActionResult> GetCoupons(
        [FromQuery] GetRevenueReportRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _reports.GetCouponsAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
