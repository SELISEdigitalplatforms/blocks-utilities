using Subscription.DomainService.Services;

namespace Subscription.DomainService.Reporting;

/// <summary>
/// Answers reporting questions for the caller's own tenant.
/// </summary>
/// <remarks>
/// Tenant-wide and never organization-wide: an operator asking what their book looks like is
/// asking across every organization they serve. The caller's organization is still resolved —
/// the context resolver fails closed and there is no path here that does not have one — but it is
/// deliberately not used as a filter. What authorizes these reads is the scope on the endpoint.
/// </remarks>
public interface ISubscriptionReportingService
{
    Task<SubscriptionOperationResult<UsageReportResponse>> GetUsageAsync(
        GetUsageReportRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<RecurringRevenueReportResponse>> GetRecurringRevenueAsync(
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<RevenueReportResponse>> GetRevenueAsync(
        GetRevenueReportRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<DunningReportResponse>> GetDunningAsync(
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<SubscriptionRosterReportResponse>> GetRosterAsync(
        GetSubscriptionReportRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<CouponReportResponse>> GetCouponsAsync(
        GetRevenueReportRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}
