using Blocks.Genesis;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to stamp both images and text into PDF
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class StampIntoPdfRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string PdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileName { get; set; } = string.Empty;
        public string MessageCoRelationId { get; set; } = string.Empty;
        public List<StampInfo> Stamps { get; set; } = new();
        public int? Engine { get; set; } = 2; // 2=PdfSharpCore (free), 3=Aspose. Engines 1 and 4 return null (no stamp support).
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public bool OpenInBrowser { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class StampInfo : BaseStamp
    {
        public int Type { get; set; } // 0 = Image, 1 = Text
        public string? ImageFileId { get; set; }
        public string? Text { get; set; }
        public string? FontName { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class StampIntoPdfResponse : BaseResponse
    {
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

