using Sms.DomainService.Requests;
using Sms.DomainService.Responses;

namespace Sms.DomainService.Services;

/// <summary>The Api side of SMS. The tenant is always the caller's, never taken from a request body.</summary>
public interface ISmsService
{
    Task<SmsMutationResponse> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default);
    Task<SmsMutationResponse> SendByTemplateAsync(SendSmsByTemplateRequest request, CancellationToken cancellationToken = default);
    Task<SmsMutationResponse> SaveProviderConfigurationAsync(SaveSmsProviderConfigurationRequest request, CancellationToken cancellationToken = default);
    Task<SmsProviderConfigurationResponse> GetProviderConfigurationAsync(CancellationToken cancellationToken = default);
}
