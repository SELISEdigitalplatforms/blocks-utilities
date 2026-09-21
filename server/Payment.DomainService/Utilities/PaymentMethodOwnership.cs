using Payment.DomainService.Entities;

namespace Payment.DomainService.Utilities;

/// <summary>
/// Which organization a card saved by a payment belongs to.
/// </summary>
/// <remarks>
/// Usually the payment's own organization. Not for a subscription: one tenant sells to many
/// organizations through a single merchant scope, so every subscriber's payment carries that
/// merchant in <see cref="PaymentDetail.OrganizationId"/>. Filing the card there put every
/// subscriber's card in one pile: the billing account, which looks its card up by the subscriber,
/// never found it and a zero-amount signup never activated, while the same card saved for a
/// second organization was merged into the first organization's record.
/// <para>
/// Everything that stamps a card and everything that looks a returning shopper's card up for a
/// payment reads this, so the two cannot disagree again.
/// </para>
/// </remarks>
public static class PaymentMethodOwnership
{
    public static string? OrganizationOf(PaymentDetail payment) =>
        string.IsNullOrWhiteSpace(payment.PaymentMethodOwnerOrganizationId)
            ? payment.OrganizationId
            : payment.PaymentMethodOwnerOrganizationId;
}
