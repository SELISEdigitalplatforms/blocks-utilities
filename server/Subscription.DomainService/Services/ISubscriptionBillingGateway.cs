namespace Subscription.DomainService.Services;

/// <summary>
/// Charges a subscription's stored payment method for a renewal or a dunning retry.
/// </summary>
/// <remarks>
/// The seam a later Stripe Invoice integration replaces. Everything that decides <em>when</em>
/// and <em>how much</em> to charge — the renewal and dunning state machine, the amount
/// calculator — depends only on this interface, never on how the charge is actually raised.
/// </remarks>
public interface ISubscriptionBillingGateway
{
    /// <summary>Returns the payment's id on success, for support traceability.</summary>
    Task<SubscriptionOperationResult<string>> ChargeAsync(
        SubscriptionChargeRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);
}
