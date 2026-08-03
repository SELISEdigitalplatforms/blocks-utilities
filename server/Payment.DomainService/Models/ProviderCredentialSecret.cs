namespace Payment.DomainService.Models;

public sealed class ProviderCredentialSecret
{
    public string ApiKey { get; init; } = string.Empty;

    public RotatingPaymentSecret StandardWebhookHmac { get; init; } =
        new();

    public RotatingPaymentSecret TokenWebhookHmac { get; init; } =
        new();
}
