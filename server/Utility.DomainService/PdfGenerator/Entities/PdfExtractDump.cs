using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Utility.DomainService.PdfGenerator.Entities
{
    [BsonIgnoreExtraElements]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfExtractDump
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        
        public string Text { get; set; } = string.Empty;
        public string MessageCorrelationId { get; set; } = string.Empty;
        public string PdfId { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty; // RecordId
        
        // Tenant and audit fields
        public string TenantId { get; set; } = string.Empty;
        public string? CreatedBy { get; set; }
        public DateTime CreateDate { get; set; }
        public string? LastUpdatedBy { get; set; }
        public DateTime LastUpdateDate { get; set; }
        public string[]? Tags { get; set; }
    }
}

