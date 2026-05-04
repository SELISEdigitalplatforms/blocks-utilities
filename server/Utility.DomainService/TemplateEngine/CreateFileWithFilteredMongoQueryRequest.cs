using Blocks.Genesis;

namespace Utility.DomainService.TemplateEngine
{
    /// <summary>
    /// Request to create file using filtered MongoDB query
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class CreateFileWithFilteredMongoQueryRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public Guid FileId { get; set; }
        public Guid TemplateFileId { get; set; }
        public string? SubscriptionFilterId { get; set; }
        public string FileNameExtension { get; set; } = ".html";
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public bool NotifyOnProcessEnding { get; set; } = false;
        public List<FilteredMongoQueryData> FilteredMongoQueryDatas { get; set; } = new();
        public IEnumerable<MetaData> MetaDataList { get; set; } = new List<MetaData>();
        public Dictionary<string, string>? EventReferenceData { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class CreateFileWithFilteredMongoQueryResponse : BaseResponse
    {
        public string FileId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class FilteredMongoQueryData
    {
        public string Text { get; set; } = string.Empty;
        public bool? IsRoot { get; set; }
        public string EntityName { get; set; } = string.Empty;
        public string OrderBy { get; set; } = "ItemId";
        public int? PageLimit { get; set; } = 100;
        public int? PageNumber { get; set; } = 0;
        public SortOrder? SortOrder { get; set; } = TemplateEngine.SortOrder.Ascending;
        public bool FromGetFilteredComplex { get; set; }
        public bool FetchAllMatchedItem { get; set; } = false;
        public string Key { get; set; } = string.Empty;
        public bool SolveConnectionForEntity { get; set; } = false;
        public bool IsParentEntityOfConnection { get; set; } = false;
        public bool ExpandParent { get; set; } = false;
        public bool ExpandChild { get; set; } = false;
        public string[] ConnectionTags { get; set; } = Array.Empty<string>();
        public string ConnectionsAccessKey { get; set; } = "Connections";
    }

    public enum SortOrder : byte
    {
        Ascending,
        Descending
    }
}



