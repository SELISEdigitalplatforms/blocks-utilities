using Blocks.Genesis;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to convert word-processing documents (.doc, .docx, .rtf, .odt, ...) to PDF
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ConvertDocumentsToPdfRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string MessageCoRelationId { get; set; } = string.Empty;
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public List<ConvertDocumentToPdfCommand> ConvertCommands { get; set; } = new();
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ConvertDocumentToPdfCommand
    {
        /// <summary>
        /// Storage file ID of the source document.
        /// </summary>
        public string DocumentFileId { get; set; } = string.Empty;

        /// <summary>
        /// Source file name including its extension. The extension is how the converter decides
        /// whether it can read the file, so a name without one is rejected before the download.
        /// </summary>
        public string DocumentFileName { get; set; } = string.Empty;

        /// <summary>
        /// Storage file ID to write the converted PDF to.
        /// </summary>
        public string OutputPdfFileId { get; set; } = string.Empty;

        /// <summary>
        /// Name for the converted PDF. When empty, the source name is reused with a .pdf extension.
        /// </summary>
        public string? OutputPdfFileName { get; set; }

        /// <summary>
        /// Keeps interactive form fields editable in the PDF instead of flattening them.
        /// </summary>
        public bool PreserveFormFields { get; set; }

        /// <summary>
        /// Produces a PDF/A-1b file for archival. Forces full font embedding and flattens form
        /// fields, both of which the standard requires.
        /// </summary>
        public bool PdfACompliant { get; set; }

        /// <summary>
        /// Recalculates page numbers, cross-references and table-of-contents entries before
        /// rendering.
        /// </summary>
        public bool UpdateFields { get; set; }

        public bool OpenInBrowser { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ConvertDocumentsToPdfResponse : BaseResponse
    {
        public string MessageCoRelationId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
