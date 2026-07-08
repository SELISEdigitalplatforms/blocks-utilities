using MongoDB.Bson.Serialization.Attributes;

namespace Sms.DomainService.Entities;

[BsonIgnoreExtraElements]
public class SmsTemplate
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();
    public string ProjectKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = "en-US";
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedDate { get; set; } = DateTime.UtcNow;
}
