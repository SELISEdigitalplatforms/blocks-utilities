using Blocks.Genesis;

namespace Utility.DomainService.TemplateEngine
{
    /// <summary>
    /// Request to render a template with JSON data
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class RenderWithJsonRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public bool NotifyOnProcessEnding { get; set; } = false;
        public string? SubscriptionFilterId { get; set; }
        public string TemplateFileId { get; set; } = string.Empty;
        public string RenderedFileId { get; set; } = string.Empty;
        public string FileNameExtension { get; set; } = ".html";
        public string JSONString { get; set; } = string.Empty;
        public Dictionary<string, string>? EventReferenceData { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class RenderWithJsonResponse : BaseResponse
    {
        public string RenderedFileId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}


