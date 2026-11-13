using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>Moves a live subscription to a different price, mid-period, with proration.</summary>
public interface ISubscriptionPlanChangeService
{
    Task<SubscriptionOperationResult<SubscriptionResponse>> ChangePlanAsync(
        string subscriptionId,
        ChangeSubscriptionPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// What <see cref="ChangePlanAsync"/> would charge or credit right now, without applying
    /// anything.
    /// </summary>
    /// <remarks>
    /// Priced by the same calculator <see cref="ChangePlanAsync"/> uses, evaluated fresh — a plan
    /// change is never frozen ahead of confirming, so this quote holds only up to the clock, the
    /// same promise the quantity-change preview already makes. A condition that would refuse the
    /// confirm without changing the price (an incomplete billing profile, no saved payment
    /// method) is reported as a blocker here rather than as a failure; a condition that leaves no
    /// coherent price to show — an unsurvivable discount, an unknown target — still fails outright.
    /// </remarks>
    Task<SubscriptionOperationResult<SubscriptionPlanChangePreviewResponse>> PreviewPlanChangeAsync(
        string subscriptionId,
        ChangeSubscriptionPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Withdraws a plan change scheduled for the end of the paid period, leaving the current plan
    /// in place.
    /// </summary>
    /// <remarks>
    /// Nothing to undo financially — a scheduled change never charged, refunded or banked
    /// anything — so this only forgets the schedule. Refused as not found when none is held, and
    /// as a conflict when the subscription moved underneath the caller.
    /// </remarks>
    Task<SubscriptionOperationResult<SubscriptionResponse>> CancelPendingPlanChangeAsync(
        string subscriptionId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);
}
