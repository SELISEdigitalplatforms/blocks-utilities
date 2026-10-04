using Utility.DomainService.PdfIngestion;
using Utility.DomainService.PdfSignatureValidation.Entities;

namespace Utility.DomainService.PdfSignatureValidation
{
    /// <summary>
    /// Request to validate the signatures of one or more PDFs already in storage.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class ValidatePdfSignaturesRequest
    {
        /// <summary>
        /// Storage file IDs of the PDFs to validate. Each entry is answered on its own, in the order
        /// sent: a blank ID, or a repeat of one earlier in the list, is rejected without stopping the
        /// rest. Unlike ingestion, a repeat is rejected rather than silently dropped, so the response
        /// always has exactly one result per entry.
        /// </summary>
        public IReadOnlyList<string> FileIds { get; init; } = [];

        /// <summary>
        /// Optional. Identifies the request so the caller is notified as each file's validation
        /// finishes. Without it the outcome is still readable from the status endpoint.
        /// </summary>
        public string? MessageCoRelationId { get; set; }
    }

    /// <summary>Whether one entry's validation was queued, and if not, why.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class PdfSignatureValidationAcceptance
    {
        /// <summary>The ID as sent, and the one to query status with.</summary>
        public string FileId { get; set; } = string.Empty;

        /// <summary>True once the validation is recorded and queued. False means it never started.</summary>
        public bool Accepted { get; set; }

        public PdfIngestionStatus? Status { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }
    }

    /// <summary>The acknowledgement returned when a batch of validations is accepted.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class ValidatePdfSignaturesBatchResponse
    {
        public string? MessageCoRelationId { get; set; }

        /// <summary>Where to check on the files in this batch: a POST, as for ingestion.</summary>
        public string StatusUrl { get; set; } = "/pdf-signature-validations/status";

        public IReadOnlyList<PdfSignatureValidationAcceptance> Results { get; init; } = [];

        public int AcceptedCount { get; set; }

        public int RejectedCount { get; set; }
    }

    /// <summary>
    /// Request to read the validation state of one or more files. A POST for the same reason
    /// <c>GetPdfIngestionStatusRequest</c> is: the query carries a list.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class GetPdfSignatureValidationStatusRequest
    {
        /// <summary>
        /// As for ingestion's status endpoint: blank IDs are ignored and a repeated ID is answered
        /// once, so there is one result per distinct ID asked about.
        /// </summary>
        public IReadOnlyList<string> FileIds { get; init; } = [];
    }

    /// <summary>One file's validation state and, once it has completed, its verdict.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class PdfSignatureValidationStatusResult
    {
        public string FileId { get; set; } = string.Empty;

        /// <summary>
        /// False when this file was never submitted for validation by the caller's tenant. Every
        /// other field is null or false in that case.
        /// </summary>
        public bool Found { get; set; }

        public PdfIngestionStatus? Status { get; set; }

        /// <summary>True once <see cref="Status"/> is <c>Completed</c> or <c>Failed</c> and can no longer change.</summary>
        public bool IsComplete { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }

        public DateTime? RequestedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        /// <summary>Present once <see cref="Status"/> is <c>Completed</c>.</summary>
        public PdfSignatureValidationVerdict? Verdict { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class PdfSignatureValidationStatusBatchResponse
    {
        public IReadOnlyList<PdfSignatureValidationStatusResult> Results { get; init; } = [];
    }
}
