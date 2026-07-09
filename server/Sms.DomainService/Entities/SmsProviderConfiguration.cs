using MongoDB.Bson.Serialization.Attributes;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Entities;

[BsonIgnoreExtraElements]
public class SmsProviderConfiguration
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();
    public string ProjectKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SmsProviderType ProviderType { get; set; }
    public bool IsDefault { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public string Sender { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string? MessagingProfileId { get; set; }
    public string? StatusCallbackBaseUrl { get; set; }
    public int MaxRetryAttempts { get; set; } = 5;
    public int RateLimitMaxPerWindow { get; set; } = 30;
    public int RateLimitWindowSeconds { get; set; } = 60;
    public int DeliveryCheckDelayMinutes { get; set; } = 10;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedDate { get; set; } = DateTime.UtcNow;
}
