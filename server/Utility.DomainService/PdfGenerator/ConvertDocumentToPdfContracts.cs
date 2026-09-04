using Utility.DomainService.PdfGenerator.Entities;

namespace Utility.DomainService.PdfGenerator
{
    /// <summary>
    /// Request to convert one or more word-processing documents (.doc, .docx, .rtf, .odt, ...) to
    /// PDF.
    /// </summary>
    /// <remarks>
    /// Each document's name, extension and directory all come from its own storage record, and the
    /// PDF replaces the source file, so a file's own ID is the only thing that has to be supplied
    /// for it.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ConvertDocumentToPdfRequest
    {
        /// <summary>
        /// Storage file IDs of the documents to convert. Each converted PDF is written back to its
        /// own ID under a .pdf name, so anything already referencing an ID ends up pointing at the
        /// PDF — and that same ID is what the status endpoint is queried with. A duplicate ID in the
        /// list is treated as one request for that file, not two.
        /// </summary>
        public List<string> FileIds { get; set; } = new();

        /// <summary>
        /// Optional. Identifies the request so the caller is notified as each conversion finishes.
        /// Leaving it empty skips the notification; the conversions still run and their outcomes are
        /// still readable from the status endpoint.
        /// </summary>
        public string? MessageCoRelationId { get; set; }
    }

    /// <summary>
    /// Whether one file's conversion was queued, and if not, why.
    /// </summary>
    /// <remarks>
    /// A batch request accepts or rejects each file independently — one file with a blank ID does
    /// not stop the rest of the batch from being queued. This is that per-file outcome, held inside
    /// <see cref="ConvertDocumentsToPdfBatchResponse"/> rather than surfaced as an HTTP-level
    /// failure, because a single response can only carry one status code for the batch as a whole.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class DocumentConversionAcceptance
    {
        /// <summary>
        /// The file this outcome is about — the same ID that was sent in, and the one to query
        /// status with. No second identifier is issued: the caller already has this one.
        /// </summary>
        public string FileId { get; set; } = string.Empty;

        /// <summary>
        /// True once the conversion has been recorded and queued. False means it never started —
        /// <see cref="ErrorCode"/> says why, and there is nothing to poll for this file.
        /// </summary>
        public bool Accepted { get; set; }

        public DocumentConversionStatus? Status { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// The acknowledgement returned when a batch of conversions is accepted.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class ConvertDocumentsToPdfBatchResponse
    {
        public string? MessageCoRelationId { get; set; }

        /// <summary>
        /// Where to check on the files in this batch — a POST, because the query needs to name the
        /// files it is asking about and a URL alone cannot carry a list.
        /// </summary>
        public string StatusUrl { get; set; } = "/document-conversions/status";

        public List<DocumentConversionAcceptance> Results { get; set; } = new();

        public int AcceptedCount { get; set; }

        public int RejectedCount { get; set; }
    }

    /// <summary>
    /// Request to read the conversion state of one or more files.
    /// </summary>
    /// <remarks>
    /// A POST rather than a GET, even though this only reads: the query needs to carry a list of
    /// file IDs, and a body on a GET is inconsistently supported by clients, proxies and framework
    /// model binding. The same trade the batch convert endpoint already makes for its own list.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class GetDocumentConversionStatusRequest
    {
        public List<string> FileIds { get; set; } = new();
    }

    /// <summary>
    /// One file's conversion state, and how to fetch the result once it has one.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class DocumentConversionStatusResult
    {
        /// <summary>
        /// The file this is about. Conversion happens in place, so this is both what was queried and
        /// where the PDF now lives.
        /// </summary>
        public string FileId { get; set; } = string.Empty;

        /// <summary>
        /// False when this file was never submitted for conversion. <see cref="Status"/> and the
        /// timestamps are meaningless in that case and are left null — there is no conversion for
        /// them to describe.
        /// </summary>
        public bool Found { get; set; }

        /// <summary>
        /// The file's name as it currently stands: the document's name while the conversion is
        /// queued or running, the .pdf name once it has succeeded.
        /// </summary>
        public string? FileName { get; set; }

        public string? MessageCoRelationId { get; set; }

        public DocumentConversionStatus? Status { get; set; }

        /// <summary>
        /// True once <see cref="Status"/> can no longer change, so a poller knows to stop asking
        /// about this file.
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

        public DateTime? RequestedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class DocumentConversionStatusBatchResponse
    {
        public List<DocumentConversionStatusResult> Results { get; set; } = new();
    }
}
