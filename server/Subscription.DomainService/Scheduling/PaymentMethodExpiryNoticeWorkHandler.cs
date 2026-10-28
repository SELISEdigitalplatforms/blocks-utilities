using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace Subscription.DomainService.Scheduling;

/// <summary>Mails billing contacts whose card on file expires within the notice window.</summary>
/// <remarks>
/// A courtesy, not money: a missed pass delays a warning, and the renewal that follows still
/// fails or succeeds on its own terms. The service marks each card it has told, so a retried item
/// sends nothing twice.
/// </remarks>
public sealed class PaymentMethodExpiryNoticeWorkHandler : ISubscriptionWorkHandler
{
    private readonly IPaymentMethodExpiryEmailService _emails;

    public PaymentMethodExpiryNoticeWorkHandler(IPaymentMethodExpiryEmailService emails) =>
        _emails = emails;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.PaymentMethodExpiryNotice;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        await _emails.NotifyAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}
