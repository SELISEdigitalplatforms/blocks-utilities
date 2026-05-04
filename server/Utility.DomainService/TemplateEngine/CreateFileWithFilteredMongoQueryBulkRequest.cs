using Blocks.Genesis;

namespace Utility.DomainService.TemplateEngine
{
    /// <summary>
    /// Bulk request to create multiple files using filtered MongoDB queries
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class CreateFileWithFilteredMongoQueryBulkRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string? SubscriptionFilterId { get; set; }
        public bool NotifyOnProcessEnding { get; set; } = false;
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public List<CreateFileWithFilteredMongoQueryData> DataList { get; set; } = new();
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class CreateFileWithFilteredMongoQueryBulkResponse : BaseResponse
    {
        public List<string> FileIds { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class CreateFileWithFilteredMongoQueryData
    {
        public Guid FileId { get; set; }
        public Guid TemplateFileId { get; set; }
        public string FileNameExtension { get; set; } = ".html";
        public List<FilteredMongoQueryData> FilteredMongoQueryDatas { get; set; } = new();
        public List<MetaData> MetaDataList { get; set; } = new();
    }
}


