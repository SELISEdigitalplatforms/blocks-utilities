using Blocks.Genesis;

namespace Utility.DomainService.TemplateEngine
{
    /// <summary>
    /// Bulk request to render multiple templates with JSON data
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class RenderWithJsonBulkRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string ReferenceId { get; set; } = string.Empty;
        public string? SubscriptionFilterId { get; set; }
        public bool NotifyOnProcessEnding { get; set; } = false;
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public List<RenderWithJsonPayload> Payloads { get; set; } = new();
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class RenderWithJsonBulkResponse : BaseResponse
    {
        public string ReferenceId { get; set; } = string.Empty;
        public List<string> RenderedFileIds { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class RenderWithJsonPayload
    {
        public string TemplateFileId { get; set; } = string.Empty;
        public string RenderedFileId { get; set; } = string.Empty;
        public string JSONString { get; set; } = string.Empty;
        public string FileNameExtension { get; set; } = ".html";
    }
}


