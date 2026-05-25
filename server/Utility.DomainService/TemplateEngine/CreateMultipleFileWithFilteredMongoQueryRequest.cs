using Blocks.Genesis;

namespace Utility.DomainService.TemplateEngine
{
    /// <summary>
    /// Request to create multiple files based on saved query configurations
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class CreateMultipleFileWithFilteredMongoQueryRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public Guid RequestId { get; set; }
        public Guid? TemplateFileId { get; set; }
        public string? SubscriptionFilterId { get; set; }
        public string FileNameExtension { get; set; } = ".html";
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public bool NotifyOnProcessEnding { get; set; } = false;
        public Dictionary<string, string>? EventReferenceData { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class CreateMultipleFileWithFilteredMongoQueryResponse : BaseResponse
    {
        public List<string> FileIds { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }
}


