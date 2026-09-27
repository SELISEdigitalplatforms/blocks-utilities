using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface IUsageRecordingService
{
    /// <summary>
    /// Records usage against a meter and reports where that leaves the allowance.
    /// </summary>
    /// <remarks>
    /// This, not the entitlement endpoint, is the enforcement point. The balance it returns
    /// already includes the caller's own contribution, so two callers arriving at the boundary
    /// together get different answers.
    /// </remarks>
    Task<SubscriptionOperationResult<UsageResponse>> RecordAsync(
        RecordUsageRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <param name="organizationId">
    /// An organization named by the caller, if any. Trusted only for the platform console — see
    /// <see cref="Subscription.DomainService.Requests.CreateSubscriptionRequest.OrganizationId"/>
    /// for the full rule.
    /// </param>
    Task<SubscriptionOperationResult<IReadOnlyList<UsageResponse>>> GetCurrentUsageAsync(
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The same current usage, with the choice of source and the diagnostics describing it.
    /// </summary>
    /// <remarks>
    /// Added beside <see cref="GetCurrentUsageAsync"/> rather than replacing it, so the existing
    /// signature and everything calling it keep working unchanged.
    /// </remarks>
    Task<SubscriptionOperationResult<UsageCurrentRead>> ReadCurrentAsync(
        string? organizationId,
        UsageReadMode readMode,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// What the caller themselves may spend: their own seats' allowances, and their organization's
    /// for every meter no seat of theirs covers.
    /// </summary>
    /// <remarks>
    /// A separate read rather than a flag on <see cref="ReadCurrentAsync"/>, because the two answer
    /// different questions. That one answers for the organization's subscription as a whole and is
    /// built around exactly one of them — it counts the meter-windows the plan should have and
    /// refuses a projection holding fewer, a judgement with no meaning spread across two plans.
    /// <para>
    /// One item per meter, chosen the same way a recording chooses: a seat first, the organization
    /// otherwise. Anything else would show somebody a balance they are not the one spending.
    /// Always from the counters, because there is no per-seat projection to prefer.
    /// </para>
    /// </remarks>
    Task<SubscriptionOperationResult<IReadOnlyList<UsageResponse>>> ReadMineAsync(
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);
}
