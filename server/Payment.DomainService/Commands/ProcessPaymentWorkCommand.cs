namespace Payment.DomainService.Commands;

public sealed class ProcessPaymentWorkCommand
{
    public string TenantId { get; init; } = string.Empty;

    public bool IncludeRecovery { get; init; }
}
