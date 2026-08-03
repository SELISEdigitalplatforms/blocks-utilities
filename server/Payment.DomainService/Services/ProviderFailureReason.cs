namespace Payment.DomainService.Services;

public sealed record ProviderFailureReason(
    string Code,
    string Summary);
