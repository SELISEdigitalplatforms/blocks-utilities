namespace Payment.DomainService.Models;

public sealed class RotatingPaymentSecret
{
    public string Active { get; init; } = string.Empty;

    public string? Previous { get; init; }
}
