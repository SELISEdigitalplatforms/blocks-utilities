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
    public string ProjectKey { get; set; } = string.Empty;
    public string[] DestinationNumbers { get; set; } = [];
    public string MessageText { get; set; } = string.Empty;
    public string? TemplateName { get; set; }
    public string? Language { get; set; }
    public Dictionary<string, string> DataContext { get; set; } = [];
    public SmsProviderType? ProviderType { get; set; }
    public string? ProviderMessageId { get; set; }
    public SmsMessageStatus Status { get; set; } = SmsMessageStatus.Accepted;
    public SmsRiskLevel RiskLevel { get; set; } = SmsRiskLevel.Low;
    public List<string> RiskReasons { get; set; } = [];
    public int AttemptCount { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedDate { get; set; } = DateTime.UtcNow;
}
