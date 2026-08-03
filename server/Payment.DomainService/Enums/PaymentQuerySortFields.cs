namespace Payment.DomainService.Enums;

public static class PaymentQuerySortFields
{
    public const string ProviderName = "providerName";
    public const string Amount = "amount";
    public const string PaymentDate = "paymentDate";
    public const string PaymentStatus = "paymentStatus";

    public static readonly string[] All =
    [
        ProviderName,
        Amount,
        PaymentDate,
        PaymentStatus
    ];
}
