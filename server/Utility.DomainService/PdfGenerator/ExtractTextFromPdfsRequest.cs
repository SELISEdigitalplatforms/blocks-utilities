using Blocks.Genesis;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to identify all text within a PDF file and store in database
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ExtractTextFromPdfsRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string MessageCoRelationId { get; set; } = string.Empty;
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public int Engine { get; set; }
        public List<ExtractTextCommand> ExtractTextCommands { get; set; } = new();
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class ExtractTextCommand
    {
        public string PdfFileId { get; set; } = string.Empty;
        public string RecordId { get; set; } = string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class ExtractTextFromPdfsResponse : BaseResponse
    {
        public string MessageCoRelationId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

