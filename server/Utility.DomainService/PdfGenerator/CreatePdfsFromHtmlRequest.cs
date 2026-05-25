using Blocks.Genesis;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to export a webpage to PDF
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class CreatePdfsFromHtmlRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string MessageCoRelationId { get; set; } = string.Empty;
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public List<CreateFromHtmlCommand> CreateFromHtmlCommands { get; set; } = new();
        public int? Engine { get; set; } = 1; // Default to Puppeteer
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class CreateFromHtmlCommand
    {
        public string HtmlFileId { get; set; } = string.Empty;
        public string? FooterHtmlFileId { get; set; }
        public string? HeaderHtmlFileId { get; set; }
        public string? DirectoryId { get; set; }
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileName { get; set; } = string.Empty;
        public double FooterHeight { get; set; }
        public double HeaderHeight { get; set; }
        public string? FirstPageHeaderFileId { get; set; }
        public string? FirstPageFooterFileId { get; set; }
        public bool IsPageNumberEnabled { get; set; }
        public bool IsTotalPageCountEnabled { get; set; }
        public bool UseFormatting { get; set; }
        public int Engine { get; set; }
        public string? Profile { get; set; }
        public bool HasHeader => !string.IsNullOrWhiteSpace(HeaderHtmlFileId);
        public bool HasFooter => !string.IsNullOrWhiteSpace(FooterHtmlFileId);
        public bool HasFirstPageHeader => !string.IsNullOrWhiteSpace(FirstPageHeaderFileId);
        public bool HasFirstPageFooter => !string.IsNullOrWhiteSpace(FirstPageFooterFileId);
        public bool OpenInBrowser { get; set; } = false;
        public string? PageNumberText { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class CreatePdfsFromHtmlResponse : BaseResponse
    {
        public string MessageCoRelationId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

