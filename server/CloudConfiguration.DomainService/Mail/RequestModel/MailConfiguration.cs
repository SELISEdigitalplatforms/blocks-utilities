using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Enums;
using MongoDB.Bson.Serialization.Attributes;

namespace CloudConfiguration.DomainService.Mail.RequestModel
{
    [BsonIgnoreExtraElements]
    public class MailConfiguration : IProjectKey
    {
        public string ConfigurationName { get; set; }
        public string ConfigurationId { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public bool EnableSSL { get; set; }
        public string? SenderName { get; set; }
        public string? SenderAddress { get; set; }
        public string SenderUserName { get; set; }
        public string AccountPassword { get; set; }
        public DateTime LastUpdatedDate { get; set; }
        public string ProjectKey { get; set; }
        public bool IsInbound { get; set; }
        public MailServiceProvider Provider { get; set; }
    }

}
