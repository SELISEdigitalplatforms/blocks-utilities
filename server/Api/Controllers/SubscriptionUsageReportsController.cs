using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Api.Controllers;

/// <summary>
/// Tenant-admin metered-usage analytics: volume over time, per-organization and per-actor
/// breakdowns, and allowance/overage history — across every organization on the tenant.
/// </summary>
/// <remarks>
/// Served under <c>/api/subscription-usage/reports</c>, gated by the
/// <c>SubscriptionUsageReportReader</c> policy — its own claim, not the general authenticated-user
/// bar every other subscription read clears, because it is the one subscription read that crosses
/// organizations.
/// <para>
/// Follows <c>SubscriptionBackgroundWorkController</c>'s precedent exactly: no organization
/// resolution step, no <c>PaymentOrganizationScope.RequestMayNameOrganization</c> check. That
/// method answers "may this caller act as one specific named organization" — a narrower question
/// than "may this caller read every organization in the tenant", which the policy claim above
/// already settled. Every query here resolves the caller's own context (their own organization,
/// which is why this never reaches <c>SubscriptionContextResolver</c>'s fail-closed
/// <c>subscription_organization_missing</c> path) and then scopes strictly by
/// <c>TenantId</c>. <c>organizationId</c> on a request is a filter within that tenant, never a
/// scope escalation.
/// </para>
/// <para>
/// The report returns ids, not names: organization and user display names resolve client-side
/// through IAM, which is what keeps identity out of the billing module.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = "SubscriptionUsageReportReader")]
[Route("subscription-usage/reports")]
public sealed class SubscriptionUsageReportsController : ControllerBase
{
    private readonly ISubscriptionUsageReportService _reports;

    public SubscriptionUsageReportsController(ISubscriptionUsageReportService reports) =>
        _reports = reports;

    /// <summary>Volume per bucket at the requested granularity.</summary>
    [HttpGet("timeseries")]
    [ProducesResponseType(typeof(ApiResponse<UsageTimeseriesResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UsageTimeseriesResponse>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTimeseries(
        [FromQuery] GetUsageReportRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _reports.GetTimeseriesAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>Per-organization totals, sortable by consumption.</summary>
    [HttpGet("organizations")]
    [ProducesResponseType(
        typeof(ApiResponse<UsageOrganizationBreakdownResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<UsageOrganizationBreakdownResponse>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetOrganizations(
        [FromQuery] GetUsageReportRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _reports.GetOrganizationsAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>Per-user totals within one organization.</summary>
    [HttpGet("actors")]
    [ProducesResponseType(typeof(ApiResponse<UsageActorBreakdownResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UsageActorBreakdownResponse>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetActors(
        [FromQuery] GetUsageReportRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _reports.GetActorsAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>Per-period allowance, plan, footprint and overage.</summary>
    [HttpGet("allowances")]
    [ProducesResponseType(typeof(ApiResponse<UsageAllowanceHistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<UsageAllowanceHistoryResponse>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAllowances(
        [FromQuery] GetUsageReportRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _reports.GetAllowancesAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
