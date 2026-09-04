using Blocks.Genesis;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to convert word-processing documents (.doc, .docx, .rtf, .odt, ...) to PDF in place
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ConvertDocumentsToPdfRequest : IProjectKey
    {
        /// <summary>
        /// Optional. Defaults to the tenant on the ambient context; supply it only for a call made
        /// on behalf of another project.
        /// </summary>
        public string? ProjectKey { get; set; }

        /// <summary>
        /// Optional. Identifies the request so the caller can be notified when the batch finishes.
        /// Leaving it empty skips the completion notification; the conversion still runs.
        /// </summary>
        public string MessageCoRelationId { get; set; } = string.Empty;

        /// <summary>
        /// Optional data echoed back with the completion event.
        /// </summary>
        public Dictionary<string, string>? EventReferenceData { get; set; }

        public List<ConvertDocumentToPdfCommand> ConvertCommands { get; set; } = new();
    }

    /// <summary>
    /// One document to convert. The PDF replaces the source file, so the file's own ID is the only
    /// thing that has to be supplied.
    /// </summary>
    /// <remarks>
    /// The name, extension and directory all come from the file's storage record: it already holds
    /// them, so asking a caller to repeat them only creates a way for the request and storage to
    /// disagree about what the file is.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ConvertDocumentToPdfCommand
    {
        /// <summary>
        /// Storage file ID of the document. The converted PDF is written back to this same ID and
        /// the file is renamed to a .pdf extension, so the original document is replaced.
        /// </summary>
        public string DocumentFileId { get; set; } = string.Empty;

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
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ConvertDocumentsToPdfResponse : BaseResponse
    {
        public string MessageCoRelationId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
