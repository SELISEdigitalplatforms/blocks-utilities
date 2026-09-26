using MongoDB.Bson.Serialization.Attributes;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Entities;

[BsonIgnoreExtraElements]
public class SmsMessage
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public List<SmsRecipient> Recipients { get; set; } = [];
    public string MessageText { get; set; } = string.Empty;
    public string? TemplateName { get; set; }
    public string? Language { get; set; }
    public Dictionary<string, string> DataContext { get; set; } = [];
    public SmsProviderType? ProviderType { get; set; }
    public SmsMessageStatus Status { get; set; } = SmsMessageStatus.Accepted;
    public SmsRiskLevel RiskLevel { get; set; } = SmsRiskLevel.Low;
    public List<string> RiskReasons { get; set; } = [];

    /// <summary>Send rounds so far. Bounded by the provider configuration's MaxRetryAttempts.</summary>
    public int AttemptCount { get; set; }
    public int DeliveryCheckCount { get; set; }

    /// <summary>
    /// Who is sending right now. A worker only sends after it has swapped its own lease in, so a
    /// redelivered command or a second replica cannot send the same message in parallel.
    /// </summary>
    public string? LeaseId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }

    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedDate { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public class SmsRecipient
{
    public string Number { get; set; } = string.Empty;
    public SmsRecipientStatus Status { get; set; } = SmsRecipientStatus.Pending;
    public string? ProviderMessageId { get; set; }
    public int Attempts { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime LastUpdatedDate { get; set; } = DateTime.UtcNow;
}
