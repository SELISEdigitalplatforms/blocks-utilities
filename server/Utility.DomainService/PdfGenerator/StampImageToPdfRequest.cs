using Blocks.Genesis;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to add an image within PDF file
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class StampImageToPdfRequest 
    {
        public string PdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileName { get; set; } = string.Empty;
        public string MessageCoRelationId { get; set; } = string.Empty;
        public List<Stamp> Stamps { get; set; } = new();
        public int? Engine { get; set; } = 2; // 2=PdfSharpCore (free), 3=Aspose. Engines 1 and 4 return null (no stamp support).
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public bool OpenInBrowser { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class Stamp : BaseStamp
    {
        public string ImageFileId { get; set; } = string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class Coordinate
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public int PageNumber { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class BaseStamp
    {
        public List<Coordinate> Coordinates { get; set; } = new();
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class StampImageToPdfResponse : BaseResponse
    {
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

