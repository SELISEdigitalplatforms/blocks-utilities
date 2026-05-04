using Blocks.Genesis;

namespace Utility.DomainService.TemplateEngine
{
    /// <summary>
    /// Bulk request to generate multiple rendered files
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class GenerateRenderedFilesBulkRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string? BulkSubscriptionFilterId { get; set; }
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public List<GenerateRenderedFileRequest> GenerateRenderedFileRequests { get; set; } = new();
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class GenerateRenderedFilesBulkResponse : BaseResponse
    {
        public List<string> FileIds { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }
}


