using Blocks.Genesis;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to export webpage to PDF using template engine in bulk
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class CreatePdfsFromHtmlUsingTEBulkRequest 
    {
        public string MessageCoRelationId { get; set; } = string.Empty;
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public List<CreateFromHtmlUsingTEForBulkCommand> CreateFromHtmlCommands { get; set; } = new();
        public bool RaiseEventOnProcessEnding { get; set; } = true;
        public bool NotifyOnProcessEnding { get; set; } = false;
        public int? Engine { get; set; } = 1; // Default to Puppeteer
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class CreateFromHtmlUsingTEForBulkCommand : CreateFromHtmlUsingTECommand
    {
        public string FileNameExtension { get; set; } = ".html";
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class CreatePdfsFromHtmlUsingTEBulkResponse : BaseResponse
    {
        public string MessageCoRelationId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

