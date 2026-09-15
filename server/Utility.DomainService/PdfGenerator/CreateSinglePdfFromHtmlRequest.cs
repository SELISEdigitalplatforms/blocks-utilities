using Blocks.Genesis;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to render a single HTML document to PDF synchronously and store the result.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class CreateSinglePdfFromHtmlRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string HtmlContent { get; set; } = string.Empty;
        public string OutputFileName { get; set; } = string.Empty;
        public string? OutputFileId { get; set; }
        public string? AccessModifier { get; set; }
        public string? HeaderHtml { get; set; }
        public string? FooterHtml { get; set; }
        public string? HeaderHeight { get; set; }
        public string? FooterHeight { get; set; }
        public bool IsPageNumberEnabled { get; set; }
        public bool IsTotalPageCountEnabled { get; set; }
        public string? PageNumberText { get; set; }
        public string? MessageCoRelationId { get; set; }
        public object? EventReferenceData { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class CreateSinglePdfFromHtmlResponse : BaseResponse
    {
        public string? FileId { get; set; }
        public string? MessageCoRelationId { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
