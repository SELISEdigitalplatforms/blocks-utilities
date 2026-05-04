using Blocks.Genesis;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to stamp text to PDF
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class StampTextToPdfRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string PdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileName { get; set; } = string.Empty;
        public string MessageCoRelationId { get; set; } = string.Empty;
        public List<StampText> Stamps { get; set; } = new();
        public int? Engine { get; set; } = 2; // 2=PdfSharpCore (free), 3=Aspose. Engines 1 and 4 return null (no stamp support).
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public bool OpenInBrowser { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class StampText : BaseStamp
    {
        public string Text { get; set; } = string.Empty;
        public string? FontName { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class StampTextToPdfResponse : BaseResponse
    {
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

