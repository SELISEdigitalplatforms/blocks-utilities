using Utility.DomainService.PdfGenerator.Entities;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Interface for PDF engine implementations
    /// </summary>
    public interface IPdfEngine
    {
        /// <summary>
        /// Merge multiple PDF streams into one
        /// </summary>
        Task<Stream?> MergePdfsAsync(List<Stream> pdfStreams);

        /// <summary>
        /// Convert HTML to PDF
        /// </summary>
        Task<Stream?> ConvertHtmlToPdfAsync(string htmlContent, PdfGenerationOptions options);

        /// <summary>
        /// Extract text from PDF
        /// </summary>
        Task<string?> ExtractTextFromPdfAsync(Stream pdfStream);

        /// <summary>
        /// Fix/repair corrupted PDF
        /// </summary>
        Task<Stream?> FixPdfAsync(Stream pdfStream);

        /// <summary>
        /// Stamp image onto PDF at specified position
        /// </summary>
        Task<Stream?> StampImageToPdfAsync(Stream pdfStream, Stream imageStream, ImageStampOptions options);

        /// <summary>
        /// Stamp text onto PDF at specified position
        /// </summary>
        Task<Stream?> StampTextToPdfAsync(Stream pdfStream, TextStampOptions options);
    }

    /// <summary>
    /// PDF generation options
    /// </summary>
    public class PdfGenerationOptions
    {
        public string? HeaderHtml { get; set; }
        public string? FooterHtml { get; set; }
        public string? FirstPageHeaderHtml { get; set; }
        public string? FirstPageFooterHtml { get; set; }
        public double HeaderHeight { get; set; }
        public double FooterHeight { get; set; }
        public bool IsPageNumberEnabled { get; set; }
        public bool IsTotalPageCountEnabled { get; set; }
        public bool UseFormatting { get; set; }
        public bool OpenInBrowser { get; set; }
        public string? ProfileId { get; set; }
        public string? PageNumberText { get; set; }
        public PdfUtilityProfile? Profile { get; set; }
    }

    /// <summary>
    /// Image stamp options
    /// </summary>
    public class ImageStampOptions
    {
        public double XPosition { get; set; }
        public double YPosition { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Rotation { get; set; }
        public double Opacity { get; set; }
        public List<int>? PageNumbers { get; set; }
        public bool IsBackground { get; set; }
    }

    /// <summary>
    /// Text stamp options
    /// </summary>
    public class TextStampOptions
    {
        public string Text { get; set; } = string.Empty;
        public double XPosition { get; set; }
        public double YPosition { get; set; }
        public string? FontName { get; set; }
        public double FontSize { get; set; }
        public string? FontColor { get; set; }
        public double Rotation { get; set; }
        public double Opacity { get; set; }
        public List<int>? PageNumbers { get; set; }
        public bool IsBackground { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
    }

    /// <summary>
    /// Coordinate for legacy support
    /// </summary>
    public class Coordinate
    {
        public int PageNumber { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
