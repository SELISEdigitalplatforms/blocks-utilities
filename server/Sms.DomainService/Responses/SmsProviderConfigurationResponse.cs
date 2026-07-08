using Sms.DomainService.Entities;

namespace Sms.DomainService.Responses;

public class SmsProviderConfigurationResponse
{
    public bool IsSuccess { get; set; }
    public SmsProviderConfiguration? Configuration { get; set; }
    public Dictionary<string, string> Errors { get; set; } = [];
}
