namespace Subscription.DomainService.Outbox;

public interface ISubscriptionSettlementReservationProcessor
{
    /// <summary>
    /// Finishes quantity increases whose caller died between reserving the units and settling the
    /// charge, either granting what was paid for or releasing what was not.
    /// </summary>
    /// <remarks>
    /// A reservation is normally held for the length of one card authorization. One still standing
    /// minutes later belongs to a request nothing will ever come back to: a pod that was recycled
    /// mid-charge, a connection dropped between the acquirer answering and the answer being acted
    /// on. Without a sweep, that subscriber is either short the units they paid for or holding a
    /// reservation that blocks every later change with a conflict.
    /// </remarks>
    /// <returns>How many reservations were resolved.</returns>
    Task<int> RecoverStaleAsync(string tenantId, CancellationToken cancellationToken);
}
