using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// The tenant-admin metered-usage analytics report: volume over time, per-organization and
/// per-actor breakdowns, and allowance/overage history — every one of them read from the
/// precomputed rollup collections rather than the live usage ledger.
/// </summary>
/// <remarks>
/// Every query here is scoped by the caller's <c>TenantId</c> alone. <c>OrganizationId</c> on a
/// request is a filter within that tenant, never a scope escalation — see
/// <c>SubscriptionUsageReportsController</c>'s own remarks.
/// </remarks>
public interface ISubscriptionUsageReportService
{
    Task<SubscriptionOperationResult<UsageTimeseriesResponse>> GetTimeseriesAsync(
        GetUsageReportRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<UsageOrganizationBreakdownResponse>> GetOrganizationsAsync(
        GetUsageReportRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<UsageActorBreakdownResponse>> GetActorsAsync(
        GetUsageReportRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<UsageAllowanceHistoryResponse>> GetAllowancesAsync(
        GetUsageReportRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}
