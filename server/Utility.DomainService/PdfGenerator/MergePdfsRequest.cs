using Blocks.Genesis;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to merge multiple PDFs into single PDF file
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class MergePdfsRequest 
    {
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileName { get; set; } = string.Empty;
        public string MessageCoRelationId { get; set; } = string.Empty;
        public int? Engine { get; set; } = 2; // Default to PDFSharp
        public List<PdfFileToBeMerged> PdfFilesToBeMerged { get; set; } = new();
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public bool OpenInBrowser { get; set; } = false;
        public bool HandleCorruptedPdf { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class PdfFileToBeMerged
    {
        public int Order { get; set; }
        public string PdfFileId { get; set; } = string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class MergePdfsResponse : BaseResponse
    {
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

