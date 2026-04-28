using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Models.Logs
{
    [BsonIgnoreExtraElements]
    public class LogProjection
    {
        public DateTime? Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string TraceId { get; set; }
        public string SpanId { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public string ActionName { get; set; } = string.Empty;
    }
}
