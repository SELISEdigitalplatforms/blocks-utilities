using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class CheckoutStatusMapper : ICheckoutStatusMapper
{
    public string Normalize(string providerStatus) =>
        providerStatus.Trim().ToLowerInvariant() switch
        {
            "completed" => "completed",
            "refused" => "refused",
            "canceled" or "cancelled" => "canceled",
            "expired" => "expired",
            "paymentpending" => "paymentPending",
            _ => "unknown"
        };

    public string ToRedirectStatus(string normalizedStatus) => normalizedStatus switch
    {
        "completed" => PaymentRedirectStatuses.Success,
        "refused" or "canceled" or "expired" => PaymentRedirectStatuses.Fail,
        _ => PaymentRedirectStatuses.Pending
    };
}
