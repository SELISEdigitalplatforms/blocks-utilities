using Blocks.Genesis;

namespace Utility.DomainService.TemplateEngine
{
    /// <summary>
    /// Request to generate a rendered file from template using entity data
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class GenerateRenderedFileRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string FileId { get; set; } = string.Empty;
        public string FileNameExtension { get; set; } = ".html";
        public string TemplateFileId { get; set; } = string.Empty;
        public IEnumerable<EntityParams> EntityIdentifierList { get; set; } = new List<EntityParams>();
        public IEnumerable<MetaData> MetaDataList { get; set; } = new List<MetaData>();
        public string? SubscriptionFilterId { get; set; }
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public Dictionary<string, string>? EventReferenceData { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class GenerateRenderedFileResponse : BaseResponse
    {
        public string FileId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class EntityParams
    {
        public string EntityName { get; set; } = string.Empty;
        public string EntityItemId { get; set; } = string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class MetaData
    {
        public string Name { get; set; } = string.Empty;
        public string? Value { get; set; }
        public List<Dictionary<string, object>>? Values { get; set; }
    }
}


