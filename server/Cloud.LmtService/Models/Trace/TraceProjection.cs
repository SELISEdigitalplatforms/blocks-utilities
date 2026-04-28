using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Models.Trace
{
    [BsonIgnoreExtraElements]
    public class TraceProjection
    {
        public DateTime Timestamp { get; set; }
        public string TraceId { get; set; }
        public string OperationName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double Duration { get; set; }
        public Dictionary<string, object?> Attributes { get; set; }
        public string ServiceName { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class SingleTraceProjection
    {
        public DateTime? Timestamp { get; set; }
        public string TraceId { get; set; } = string.Empty;
        public string SpanId { get; set; } = string.Empty;
        public string ParentSpanId { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string ActivitySourceName { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double Duration { get; set; }
        public Dictionary<string, object?> Attributes { get; set; } = new();
        public string Status { get; set; } = string.Empty;
        public string StatusDescription { get; set; } = string.Empty;
        public Dictionary<string, string> Baggage { get; set; } = new();
        public string ServiceName { get; set; } = string.Empty;
    }
}
