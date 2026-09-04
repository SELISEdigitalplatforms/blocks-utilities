using Utility.DomainService.PdfGenerator.Entities;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to convert one word-processing document (.doc, .docx, .rtf, .odt, ...) to PDF.
    /// </summary>
    /// <remarks>
    /// One document per request. The name, extension and directory all come from the file's storage
    /// record, and the PDF replaces the source file, so the file's own ID is the only thing that has
    /// to be supplied.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ConvertDocumentToPdfRequest
    {
        /// <summary>
        /// Storage file ID of the document. The converted PDF is written back to this same ID under
        /// a .pdf name, so anything already referencing the ID ends up pointing at the PDF — and the
        /// same ID is what the status endpoint is polled with.
        /// </summary>
        public string InputFileId { get; set; } = string.Empty;

        /// <summary>
        /// Optional. Identifies the request so the caller is notified when the conversion finishes.
        /// Leaving it empty skips the notification; the conversion still runs and its outcome is
        /// still readable from the status endpoint.
        /// </summary>
        public string? MessageCoRelationId { get; set; }
    }

    /// <summary>
    /// The acknowledgement returned when a conversion is accepted.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ConvertDocumentToPdfAcceptedResponse
    {
        /// <summary>
        /// The file being converted — the same ID that was sent in, and the one to poll with. No
        /// second identifier is issued: the caller already has this one.
        /// </summary>
        public string FileId { get; set; } = string.Empty;

        public string? MessageCoRelationId { get; set; }

        public DocumentConversionStatus Status { get; set; } = DocumentConversionStatus.Queued;

        /// <summary>
        /// Where to poll for this conversion's outcome.
        /// </summary>
        public string StatusUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// A conversion's current state, and how to fetch the result once it has one.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class DocumentConversionStatusResponse
    {
        /// <summary>
        /// The file this is about. Conversion happens in place, so this is both what was sent in and
        /// where the PDF now lives.
        /// </summary>
        public string FileId { get; set; } = string.Empty;

        /// <summary>
        /// The file's name as it currently stands: the document's name while the conversion is
        /// queued or running, the .pdf name once it has succeeded.
        /// </summary>
        public string? FileName { get; set; }

        public string? MessageCoRelationId { get; set; }

        public DocumentConversionStatus Status { get; set; }

        /// <summary>
        /// True once <see cref="Status"/> can no longer change, so a poller knows to stop.
        /// </summary>
        public bool IsComplete { get; set; }

        /// <summary>
        /// A time-limited URL the PDF can be downloaded from. Null until the conversion succeeds, so
        /// a caller cannot mistake the still-unconverted document for the result, and resolved fresh
        /// on each request rather than stored, because it expires.
        /// </summary>
        public string? DownloadUrl { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }

        public DateTime RequestedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }
    }
}
