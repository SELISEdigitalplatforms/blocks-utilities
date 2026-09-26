using Sms.DomainService.Entities;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Responses;

public class SmsProviderConfigurationResponse
{
    public bool IsSuccess { get; set; }
    public SmsProviderConfigurationView? Configuration { get; set; }
    public Dictionary<string, string> Errors { get; set; } = [];
}

/// <summary>What the portal may see of a configuration: everything but the secret.</summary>
public class SmsProviderConfigurationView
{
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SmsProviderType ProviderType { get; set; }
    public bool IsDefault { get; set; }
    public bool IsEnabled { get; set; }
    public string Sender { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public bool HasApiKey { get; set; }
    public string? MessagingProfileId { get; set; }
    public string? WebhookPublicKey { get; set; }
    public string? StatusCallbackBaseUrl { get; set; }
    public int MaxRetryAttempts { get; set; }
    public int DeliveryCheckDelayMinutes { get; set; }
    public SmsRateLimitSettings RateLimit { get; set; } = new();
    public SmsSpamFilterSettings SpamFilter { get; set; } = new();
    public DateTime LastUpdatedDate { get; set; }

    public static SmsProviderConfigurationView From(SmsProviderConfiguration configuration) => new()
    {
        ItemId = configuration.ItemId,
        Name = configuration.Name,
        ProviderType = configuration.ProviderType,
        IsDefault = configuration.IsDefault,
        IsEnabled = configuration.IsEnabled,
        Sender = configuration.Sender,
        AccountId = configuration.AccountId,
        HasApiKey = !string.IsNullOrWhiteSpace(configuration.ApiKeySecretId),
        MessagingProfileId = configuration.MessagingProfileId,
        WebhookPublicKey = configuration.WebhookPublicKey,
        StatusCallbackBaseUrl = configuration.StatusCallbackBaseUrl,
        MaxRetryAttempts = configuration.MaxRetryAttempts,
        DeliveryCheckDelayMinutes = configuration.DeliveryCheckDelayMinutes,
        RateLimit = configuration.RateLimit,
        SpamFilter = configuration.SpamFilter,
        LastUpdatedDate = configuration.LastUpdatedDate
    };
}
