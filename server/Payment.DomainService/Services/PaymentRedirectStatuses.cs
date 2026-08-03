using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public static class PaymentRedirectStatuses
{
    public const string Success = "success";
    public const string Fail = "fail";
    public const string Cancelled = "cancelled";
    public const string Pending = "pending";
}
