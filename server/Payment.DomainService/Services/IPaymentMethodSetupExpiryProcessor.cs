namespace Payment.DomainService.Services;

/// <summary>
/// Finding 3's terminal recovery path for the two-signal card setup state machine (see
/// <see cref="PaymentMethodSetupWebhookStateTransitionService"/>): a setup left waiting past its
/// configured timeout for a completion signal that never arrives is expired, so it stops blocking
/// its idempotency key forever and an operator or a fresh signup can see, and act on, the fact
/// that it never completed.
/// </summary>
public interface IPaymentMethodSetupExpiryProcessor
{
    Task<int> ExpireDueAsync(string tenantId, CancellationToken cancellationToken);
}
