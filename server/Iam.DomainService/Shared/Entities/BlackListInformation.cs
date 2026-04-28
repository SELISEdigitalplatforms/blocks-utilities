using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class BlackListInformation
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
