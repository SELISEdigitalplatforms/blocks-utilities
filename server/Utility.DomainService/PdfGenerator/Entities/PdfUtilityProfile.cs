using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Utility.DomainService.PdfGenerator.Entities
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfUtilityProfile
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        
        public string? MarginLeft { get; set; }
        public string? MarginRight { get; set; }
        public string? HeaderSpacing { get; set; }
        public string? FooterSpacing { get; set; }
        public string? Width { get; set; }
        public string? Height { get; set; }
        public string? Zoom { get; set; }
        public int PageNumberPosition { get; set; }
        public string? PageNumberText { get; set; }
        public int[]? PageNumberOffset { get; set; }
        public int[]? RemoveHeaderFromPage { get; set; }
        public int[]? RemoveFooterFromPage { get; set; }
        public string? PageNumberFont { get; set; }
        public bool AsyncStream { get; set; } = false;
        public bool ExecuteUsingWrapper { get; set; } = false;
        public bool RemoveHeaderFooterFromCoverPage { get; set; } = false;
        public string Orientation { get; set; } = "Portrait"; // Portrait or Landscape
        public string? WkCustomArgs { get; set; }
    }
}

