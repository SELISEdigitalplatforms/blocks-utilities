using Utility.DomainService.PdfGenerator.Tooling.Inspection;
using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfIngestion
{
    /// <summary>
    /// Request to inspect and, if needed, remediate one or more PDFs already in storage.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class IngestPdfsRequest
    {
        /// <summary>
        /// Storage file IDs of the PDFs to ingest. Remediated output overwrites its own ID, so
        /// anything already referencing an ID keeps working, and that same ID is what the status
        /// endpoint is queried with. A duplicate ID is treated as one request for that file, not two.
        /// </summary>
        public List<string> FileIds { get; set; } = [];

        /// <summary>
        /// Optional. Identifies the request so the caller is notified as each file's ingestion
        /// finishes. Leaving it empty skips the notification; ingestion still runs and its outcome is
        /// still readable from the status endpoint.
        /// </summary>
        public string? MessageCoRelationId { get; set; }

        /// <summary>
        /// When true, every file is inspected and its verdict recorded, but nothing is uploaded back
        /// to storage even when a remediation stage would otherwise have changed the file's bytes.
        /// Meant for the first production rollout of an otherwise destructive in-place feature.
        /// </summary>
        public bool DryRun { get; set; }
    }

    /// <summary>
    /// Whether one file's ingestion was queued, and if not, why.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfIngestionAcceptance
    {
        /// <summary>
        /// The file this outcome is about - the same ID that was sent in, and the one to query
        /// status with.
        /// </summary>
        public string FileId { get; set; } = string.Empty;

        /// <summary>
        /// True once the ingestion has been recorded and queued. False means it never started -
        /// <see cref="ErrorCode"/> says why, and there is nothing to poll for this file.
        /// </summary>
        public bool Accepted { get; set; }

        public PdfIngestionStatus? Status { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// The acknowledgement returned when a batch of ingestions is accepted.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class IngestPdfsBatchResponse
    {
        public string? MessageCoRelationId { get; set; }

        /// <summary>
        /// Where to check on the files in this batch - a POST, because the query needs to name the
        /// files it is asking about and a URL alone cannot carry a list.
        /// </summary>
        public string StatusUrl { get; set; } = "/pdf-ingestions/status";

        public List<PdfIngestionAcceptance> Results { get; set; } = [];

        public int AcceptedCount { get; set; }

        public int RejectedCount { get; set; }
    }

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
