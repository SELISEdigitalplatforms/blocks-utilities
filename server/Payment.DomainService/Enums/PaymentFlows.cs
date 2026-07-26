namespace Payment.DomainService.Enums;

public static class PaymentFlows
{
    public const string HostedCheckout = "HOSTED_CHECKOUT";

    public const string RecurringCharge = "RECURRING_CHARGE";

    public static readonly string[] All =
    [
        HostedCheckout,
        RecurringCharge
    ];
}
