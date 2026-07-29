using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Adyen;

public sealed class AdyenCheckoutStatusMapper : ICheckoutStatusMapper
{
    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.AdyenOnlineProvider,
            StringComparison.OrdinalIgnoreCase);

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
        "canceled" => PaymentRedirectStatuses.Cancelled,
        "refused" or "expired" => PaymentRedirectStatuses.Fail,
        _ => PaymentRedirectStatuses.Pending
    };
}
