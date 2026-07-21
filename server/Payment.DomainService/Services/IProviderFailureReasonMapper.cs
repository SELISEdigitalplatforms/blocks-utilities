namespace Payment.DomainService.Services;

public interface IProviderFailureReasonMapper
{
    ProviderFailureReason? Map(
        string? eventCode,
        bool success,
        string? providerReason);
}
