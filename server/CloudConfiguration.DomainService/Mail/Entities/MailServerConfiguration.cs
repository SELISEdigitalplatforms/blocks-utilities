using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Enums;
using MongoDB.Bson.Serialization.Attributes;

namespace CloudConfiguration.DomainService.Mail.Entities
{
    [BsonIgnoreExtraElements]
    public class MailServerConfiguration : BaseEntity
    {
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public bool EnableSSL { get; set; }
        public string SenderName { get; set; }
        public string SenderAddress { get; set; }
        public string SenderUserName { get; set; }
        public string AccountPassword { get; set; }
        public bool UseDefaultCredentials { get; set; }
        public SmtpClient SmtpClient { get; set; } = SmtpClient.Default;
        public bool IsDefault { get; set; }
        public bool IsInbound { get; set; }
        public MailServiceProvider Provider { get; set; }
    }

    public enum SmtpClient
    {
        Default = 0,
        MsGraph,
        MsMailKit
    }
}
