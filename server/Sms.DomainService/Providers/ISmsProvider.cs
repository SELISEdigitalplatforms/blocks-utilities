using Sms.DomainService.Dtos;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Providers;

public interface ISmsProvider
{
    SmsProviderType ProviderType { get; }
    Task<SmsProviderResult> SendAsync(SmsMessage message, SmsProviderConfiguration configuration, CancellationToken cancellationToken = default);
    Task<SmsProviderDeliveryStatus> GetDeliveryStatusAsync(SmsMessage message, SmsProviderConfiguration configuration, CancellationToken cancellationToken = default);
}
