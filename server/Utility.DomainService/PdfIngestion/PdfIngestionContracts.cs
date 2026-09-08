using Utility.DomainService.PdfGenerator.Tooling.Inspection;
using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfIngestion
{
    /// <summary>
    /// Request to read the ingestion state of one or more files.
    /// </summary>
    /// <remarks>
    /// A POST rather than a GET, even though this only reads: the query needs to carry a list of
    /// file IDs, and a body on a GET is inconsistently supported by clients, proxies and framework
    /// model binding. The same trade the document conversion status endpoint already makes.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class GetPdfIngestionStatusRequest
    {
        public List<string> FileIds { get; set; } = [];
    }

    /// <summary>
    /// One file's ingestion state and, once the pipeline has run, its full verdict: what shape the
    /// file arrived in, what was repaired, and what still needs attention.
    /// </summary>
    /// <remarks>
    /// Every field below <see cref="Status"/> is null when <see cref="Found"/> is false or the job
    /// has not reached <see cref="PdfIngestionStatus.Completed"/> yet - there is no verdict for them
    /// to describe before the pipeline has actually run.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfIngestionStatusResult
    {
        /// <summary>
        /// The file this is about. Remediation happens in place, so this is both what was queried
        /// and where the (possibly rewritten) PDF now lives.
        /// </summary>
        public string FileId { get; set; } = string.Empty;

        /// <summary>
        /// False when this file was never submitted for ingestion. <see cref="Status"/> and every
        /// verdict field below are meaningless in that case and are left null.
        /// </summary>
        public bool Found { get; set; }

        public string? FileName { get; set; }

        public string? MessageCoRelationId { get; set; }

        public PdfIngestionStatus? Status { get; set; }

        /// <summary>
        /// True once <see cref="Status"/> can no longer change, so a poller knows to stop asking
        /// about this file.
        /// </summary>
        public bool IsComplete { get; set; }

        /// <summary>
        /// A time-limited URL the current file can be downloaded from. Resolved fresh on each
        /// request rather than stored, because it expires.
        /// </summary>
        public string? DownloadUrl { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }

        public DateTime? RequestedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        public int? PageCount { get; set; }

        // Readability
        public bool? WasReadable { get; set; }
        public bool? IsReadable { get; set; }
        public bool? RepairedWithQpdf { get; set; }

        // Geometry
        public bool? GeometryWasReadable { get; set; }
        public bool? GeometryIsReadable { get; set; }
        public bool? GeometryNormalized { get; set; }
        public IReadOnlyList<PdfPageGeometry>? PageGeometry { get; set; }
        public string? GeometrySkippedReason { get; set; }

        // Signature
        public bool? HasSignature { get; set; }
        public int? SignatureCount { get; set; }

        // PDF/A
        public PdfAMetadataClaim? MetadataClaim { get; set; }
        public string? ClaimedProfile { get; set; }
        public string? ValidatedProfile { get; set; }
        public bool? IsPdfA { get; set; }
        public bool? IsCompliant { get; set; }
        public IReadOnlyList<string>? PdfAFailedChecks { get; set; }
        public bool? PdfARepairAttempted { get; set; }
        public bool? PdfARepairSucceeded { get; set; }
        public string? FinalProfile { get; set; }

        /// <summary>Whether any pipeline stage actually rewrote the file's bytes.</summary>
        public bool? ContentChanged { get; set; }

        /// <summary>Which stages ran and what each one's outcome was, in the order they ran.</summary>
        public IReadOnlyList<PdfIngestionStageRecord>? Stages { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfIngestionStatusBatchResponse
    {
        public List<PdfIngestionStatusResult> Results { get; set; } = [];
    }
}
