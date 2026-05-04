using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Notification
{
    [BsonIgnoreExtraElements]
    public class NotificationConnection
    {
        [BsonId]
        public string Id { get; set; }
        public string ConnectionId { get; set; }
        public string? UserId { get; set; }
        public DateTime CreatedTime { get; set; } = DateTime.Now;
    }
}
