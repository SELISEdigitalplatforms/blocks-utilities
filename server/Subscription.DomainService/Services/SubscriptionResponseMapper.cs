using Subscription.DomainService.Entities;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public sealed class SubscriptionResponseMapper : ISubscriptionResponseMapper
{
    public SubscriptionResponse ToResponse(
        SubscriptionDetail subscription,
        string? checkoutUrl = null)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return new SubscriptionResponse
        {
            SubscriptionId = subscription.ItemId,
            Status = subscription.Status.ToString(),
            PlanCode = subscription.Plan.Code,
            PlanName = subscription.Plan.DisplayName,
            CurrencyCode = subscription.CurrencyCode,
            UnitAmountMinor = subscription.Price.UnitAmountMinor,
            Interval = subscription.Price.Interval.ToString(),
            IntervalCount = subscription.Price.IntervalCount,
            DisplayPriceNote = subscription.Price.DisplayPriceNote,
            Quantities = subscription.QuantityItems
                .Select(item => new SubscriptionQuantityResponse
                {
                    ItemKey = item.ItemKey,
                    UnitLabel = item.UnitLabel,
                    Quantity = item.Quantity
                })
                .ToList(),
            CurrentPeriodStartUtc = subscription.CurrentPeriodStartUtc,
            CurrentPeriodEndUtc = subscription.CurrentPeriodEndUtc,
            NextPaymentAtUtc = subscription.NextFeeBillingAtUtc,
            TrialEndsAtUtc = subscription.Trial?.EndsAtUtc,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            CanceledAtUtc = subscription.CanceledAtUtc,
            CheckoutUrl = checkoutUrl,
            Version = subscription.Version
        };
    }
}
