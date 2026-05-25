namespace Utility.DomainService.TemplateEngine.Events
{
    /// <summary>
    /// Event for rendering template with JSON
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record RenderWithJsonEvent
    {
        public string TemplateFileId { get; set; } = string.Empty;
        public string RenderedFileId { get; set; } = string.Empty;
        public string JSONString { get; set; } = string.Empty;
        public string FileNameExtension { get; set; } = ".html";
        public string? SubscriptionFilterId { get; set; }
        public string? ProjectKey { get; set; }
        public bool NotifyOnProcessEnding { get; set; } = false;
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public Dictionary<string, string>? EventReferenceData { get; set; }
    }

    /// <summary>
    /// Event for bulk rendering templates with JSON
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record RenderWithJsonBulkEvent
    {
        public string ReferenceId { get; set; } = string.Empty;
        public string? SubscriptionFilterId { get; set; }
        public string? ProjectKey { get; set; }
        public List<RenderWithJsonPayload> Payloads { get; set; } = new();
        public bool NotifyOnProcessEnding { get; set; } = false;
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public Dictionary<string, string>? EventReferenceData { get; set; }
    }

    /// <summary>
    /// Event for generating rendered file from entity data
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record GenerateRenderedFileEvent
    {
        public string FileId { get; set; } = string.Empty;
        public string TemplateFileId { get; set; } = string.Empty;
        public string FileNameExtension { get; set; } = ".html";
        public List<EntityParams> EntityIdentifierList { get; set; } = new();
        public List<MetaData> MetaDataList { get; set; } = new();
        public string? SubscriptionFilterId { get; set; }
        public string? ProjectKey { get; set; }
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public Dictionary<string, string>? EventReferenceData { get; set; }
    }

    /// <summary>
    /// Event for bulk generating rendered files
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record GenerateRenderedFilesBulkEvent
    {
        public string? BulkSubscriptionFilterId { get; set; }
        public string? ProjectKey { get; set; }
        public List<GenerateRenderedFileRequest> GenerateRenderedFileRequests { get; set; } = new();
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public Dictionary<string, string>? EventReferenceData { get; set; }
    }

    /// <summary>
    /// Event for creating file with filtered MongoDB query
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record CreateFileWithFilteredMongoQueryEvent
    {
        public Guid FileId { get; set; }
        public Guid TemplateFileId { get; set; }
        public string FileNameExtension { get; set; } = ".html";
        public List<FilteredMongoQueryData> FilteredMongoQueryDatas { get; set; } = new();
        public List<MetaData> MetaDataList { get; set; } = new();
        public string? SubscriptionFilterId { get; set; }
        public string? ProjectKey { get; set; }
        public bool NotifyOnProcessEnding { get; set; } = false;
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public Dictionary<string, string>? EventReferenceData { get; set; }
    }

    /// <summary>
    /// Event for bulk creating files with filtered MongoDB queries
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record CreateFileWithFilteredMongoQueryBulkEvent
    {
        public string? SubscriptionFilterId { get; set; }
        public string? ProjectKey { get; set; }
        public List<CreateFileWithFilteredMongoQueryData> DataList { get; set; } = new();
        public bool NotifyOnProcessEnding { get; set; } = false;
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public Dictionary<string, string>? EventReferenceData { get; set; }
    }

    /// <summary>
    /// Event for creating multiple files with filtered MongoDB queries
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record CreateMultipleFileWithFilteredMongoQueryEvent
    {
        public Guid RequestId { get; set; }
        public Guid? TemplateFileId { get; set; }
        public string FileNameExtension { get; set; } = ".html";
        public string? SubscriptionFilterId { get; set; }
        public string? ProjectKey { get; set; }
        public bool NotifyOnProcessEnding { get; set; } = false;
        public bool RaiseEventOnProcessEnding { get; set; } = false;
        public Dictionary<string, string>? EventReferenceData { get; set; }
    }
}


