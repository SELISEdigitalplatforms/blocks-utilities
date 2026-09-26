using Sms.DomainService.Entities;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Requests;

public class SaveSmsProviderConfigurationRequest
{
    /// <summary>Empty to create; an existing id to update.</summary>
    public string? ConfigurationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SmsProviderType ProviderType { get; set; }
    public bool IsDefault { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public string Sender { get; set; } = string.Empty;
    public string? AccountId { get; set; }

    /// <summary>
    /// Twilio auth token or Telnyx API key. Required on create; on update, empty keeps the stored
    /// secret and a value rotates it. Written to Blocks Secrets, never to the configuration.
    /// </summary>
    public string? ApiKey { get; set; }
    public string? MessagingProfileId { get; set; }
    public string? WebhookPublicKey { get; set; }
    public string? StatusCallbackBaseUrl { get; set; }
    public int MaxRetryAttempts { get; set; } = 5;
    public int DeliveryCheckDelayMinutes { get; set; } = 10;
    public SmsRateLimitSettings RateLimit { get; set; } = new();
    public SmsSpamFilterSettings SpamFilter { get; set; } = new();
}
