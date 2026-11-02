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

    /// <summary>
    /// The run-rate over time, read from the daily captures.
    /// </summary>
    Task<SubscriptionOperationResult<RecurringRevenueTrendResponse>> GetRecurringRevenueTrendAsync(
        GetBucketedReportRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Captures today's run-rate for a tenant unless today already has one.
    /// </summary>
    /// <remarks>
    /// Takes the tenant explicitly rather than resolving it from a request, because its caller is
    /// the repair sweep, which acts for no request. Returns whether a capture was written.
    /// </remarks>
    Task<bool> CaptureRecurringRevenueSnapshotAsync(
        string tenantId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<RevenueReportResponse>> GetRevenueAsync(
        GetBucketedReportRequest request,
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
