namespace Payment.DomainService.Models;

public sealed class TenantPaymentSecuritySecret
{
    public RotatingPaymentSecret ReturnStateHmac { get; init; } =
        new();

    public string ShopperReferenceHmacKey { get; init; } =
        string.Empty;
}
