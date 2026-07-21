using Blocks.Genesis;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to export webpage to PDF using template engine
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class CreatePdfsFromHtmlUsingTERequest
    {
        public string MessageCoRelationId { get; set; } = string.Empty;
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public List<CreateFromHtmlUsingTECommand> CreateFromHtmlCommands { get; set; } = new();
        public int? Engine { get; set; } = 1; // Default to Puppeteer
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class CreateFromHtmlUsingTECommand : CreateFromHtmlCommand
    {
        public string TemplateFileId { get; set; } = string.Empty;
        public List<GetFilteredSqlQueryData>? FilteredSqlQueryDatas { get; set; }
        public List<PdfMetaData>? MetaDataList { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class GetFilteredSqlQueryData
    {
        public string EntityName { get; set; } = string.Empty;
        public string? FilterQuery { get; set; }
        public Dictionary<string, object>? FilterParameters { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class PdfMetaData
    {
        public string Key { get; set; } = string.Empty;
        public object? Value { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class CreatePdfsFromHtmlUsingTEResponse : BaseResponse
    {
        public string MessageCoRelationId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

