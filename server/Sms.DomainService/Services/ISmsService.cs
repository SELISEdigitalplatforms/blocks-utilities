using Sms.DomainService.Requests;
using Sms.DomainService.Responses;

namespace Sms.DomainService.Services;

public interface ISmsService
{
    Task<SmsMutationResponse> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default);
    Task<SmsMutationResponse> SendByTemplateAsync(SendSmsByTemplateRequest request, CancellationToken cancellationToken = default);
    Task<SmsMutationResponse> SaveProviderConfigurationAsync(SaveSmsProviderConfigurationRequest request, CancellationToken cancellationToken = default);
    Task<SmsProviderConfigurationResponse> GetProviderConfigurationAsync(string? projectKey, CancellationToken cancellationToken = default);
    Task<SmsMutationResponse> ProcessTwilioStatusAsync(TwilioSmsStatusCallbackRequest request, CancellationToken cancellationToken = default);
    Task<SmsMutationResponse> ProcessTelnyxStatusAsync(TelnyxSmsStatusCallbackRequest request, CancellationToken cancellationToken = default);
}
