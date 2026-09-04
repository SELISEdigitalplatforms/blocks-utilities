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
        /// a .pdf name, so anything already referencing the ID ends up pointing at the PDF.
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
    /// <remarks>
    /// Carries the conversion's own ID because the work has not happened yet. That ID is what the
    /// caller polls if the completion notification never arrives.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ConvertDocumentToPdfAcceptedResponse
    {
        public string ConversionId { get; set; } = string.Empty;

        public string InputFileId { get; set; } = string.Empty;

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
        public string ConversionId { get; set; } = string.Empty;

        public string InputFileId { get; set; } = string.Empty;

        public string? MessageCoRelationId { get; set; }

        public DocumentConversionStatus Status { get; set; }

        /// <summary>
        /// True once <see cref="Status"/> can no longer change, so a poller knows to stop.
        /// </summary>
        public bool IsComplete { get; set; }

        public string? SourceFileName { get; set; }

        /// <summary>
        /// The converted file's name. Null until the conversion succeeds.
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>
        /// The storage ID holding the PDF, which is the input file ID — conversion replaces the
        /// source. Null until the conversion succeeds, so a caller cannot mistake an unconverted
        /// document for a converted one.
        /// </summary>
        public string? FileId { get; set; }

        /// <summary>
        /// A time-limited URL the PDF can be downloaded from. Null until the conversion succeeds,
        /// and resolved fresh on each request rather than stored, because it expires.
        /// </summary>
        public string? DownloadUrl { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }

        public DateTime RequestedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }
    }
}
