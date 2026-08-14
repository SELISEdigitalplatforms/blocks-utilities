using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// One status change and everything that must be written atomically with it.
/// </summary>
/// <remarks>
/// Grouped into a single value rather than passed as a long parameter list, so a caller cannot
/// set a status without saying what it expected the previous one to be. The event travels here
/// too because appending it in the same update is what makes publication survive a crash: Mongo
/// and the bus share no transaction, so a separate write would lose events precisely when
/// something went wrong.
/// </remarks>
public sealed record SubscriptionTransition(
    SubscriptionStatus ExpectedStatus,
    SubscriptionStatus NewStatus)
{
    public DateTime? ActivatedAtUtc { get; init; }

    public DateTime? CanceledAtUtc { get; init; }

    public DateTime? EndedAtUtc { get; init; }

    public bool? CancelAtPeriodEnd { get; init; }

    public string? CancellationReason { get; init; }

    public string? InitialPaymentDetailId { get; init; }

    public DateTime? CurrentPeriodStartUtc { get; init; }

    public DateTime? CurrentPeriodEndUtc { get; init; }

    public DateTime? NextFeeBillingAtUtc { get; init; }

    /// <summary>Explicitly clears the next billing instant, which a null value cannot express.</summary>
    public bool ClearNextFeeBillingAt { get; init; }

    public SubscriptionOutboxEvent? Event { get; init; }
}
