using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// Buying, granting, moving and listing meter credits on one subscription. See spec 002.
/// </summary>
public interface ISubscriptionCreditService
{
    /// <summary>What a purchase would cost, checked exactly as the purchase is. Changes nothing.</summary>
    Task<SubscriptionOperationResult<CreditQuoteResponse>> PreviewAsync(
        string subscriptionId,
        CreditPurchaseRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>Charges the stored card once for the quoted credits and grants them.</summary>
    Task<SubscriptionOperationResult<CreditPurchaseResponse>> PurchaseAsync(
        string subscriptionId,
        CreditPurchaseRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>Gives credits without a charge, with a reason.</summary>
    Task<SubscriptionOperationResult<CreditGrantResponse>> GrantAsync(
        string subscriptionId,
        GrantCreditsRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>Moves unused credits between the pool and a seat, or between two seats.</summary>
    Task<SubscriptionOperationResult<CreditMoveResponse>> MoveAsync(
        string subscriptionId,
        MoveCreditsRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <param name="poolOnly">True lists only the shared pool's grants; ignored when a seat is named.</param>
    Task<SubscriptionOperationResult<IReadOnlyList<CreditGrantResponse>>> ListGrantsAsync(
        string subscriptionId,
        string? organizationId,
        string? meterKey,
        int? seatNumber,
        bool poolOnly,
        CreditGrantStatus? status,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finishes or fails purchases a crash left at Pending or Charged, for one tenant. Run by the
    /// Worker's recovery sweep.
    /// </summary>
    Task<int> RecoverStaleAsync(string tenantId, CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<IReadOnlyList<CreditPurchaseResponse>>> ListPurchasesAsync(
        string subscriptionId,
        string? organizationId,
        CreditPurchaseStatus? status,
        string correlationId,
        CancellationToken cancellationToken);
}
