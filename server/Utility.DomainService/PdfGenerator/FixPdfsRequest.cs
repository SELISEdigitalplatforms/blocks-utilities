using Blocks.Genesis;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to fix existing corrupted PDF files
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class FixPdfsRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string MessageCorrelationId { get; set; } = string.Empty;
        public List<FixPdfCommand> PdfInfos { get; set; } = new();
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class FixPdfCommand
    {
        public string OriginalPdfId { get; set; } = string.Empty;
        public string OutputPdfId { get; set; } = string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class FixPdfsResponse : BaseResponse
    {
        public string MessageCorrelationId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

