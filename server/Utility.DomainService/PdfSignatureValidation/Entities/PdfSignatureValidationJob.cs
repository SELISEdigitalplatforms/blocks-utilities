using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Utility.DomainService.PdfIngestion;

namespace Utility.DomainService.PdfSignatureValidation.Entities
{
    /// <summary>
    /// Where the signature validation of one file has got to.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>PdfIngestionJob</c>: written when a request is accepted, updated as the worker
    /// moves through it, keyed by the file's storage ID. Re-validating a file replaces this record,
    /// so the status endpoint always answers for the latest request.
    /// <para>
    /// What ingestion does not have is <see cref="RunId"/>. A file can be re-requested while an
    /// earlier run is still in flight, and that run would otherwise finish last and overwrite the
    /// newer request's record with a stale verdict. Every worker write is conditional on the
    /// <see cref="RunId"/> it was queued with still being the current one.
    /// </para>
    /// </remarks>
    [BsonIgnoreExtraElements]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class PdfSignatureValidationJob
    {
        /// <summary>The file's storage ID, and this record's key.</summary>
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// New for every accepted request and carried on its queue message. A write whose run ID no
        /// longer matches belongs to a superseded request and is discarded.
        /// </summary>
        public string RunId { get; set; } = string.Empty;

        /// <summary>The caller's own correlation ID, used to address the completion notification.</summary>
        public string? MessageCoRelationId { get; set; }

        /// <summary>Stored as a string, as <c>PdfIngestionJob.Status</c> is.</summary>
        [BsonRepresentation(BsonType.String)]
        public PdfIngestionStatus Status { get; set; } = PdfIngestionStatus.Queued;

        public string? FileName { get; set; }

        /// <summary>Captured at accept time and carried on the event; see <c>PdfIngestionJob.UserId</c>.</summary>
        public string? UserId { get; set; }

        /// <summary>Why the job produced no verdict. Null unless <see cref="Status"/> is <see cref="PdfIngestionStatus.Failed"/>.</summary>
        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }

        public string TenantId { get; set; } = string.Empty;

        public string? CreatedBy { get; set; }

        public DateTime CreateDate { get; set; }

        public DateTime LastUpdateDate { get; set; }

        /// <summary>When the job reached a terminal state. Null while still queued or running.</summary>
        public DateTime? CompletedDate { get; set; }

        /// <summary>The validator's verdict, once <see cref="Status"/> reaches <see cref="PdfIngestionStatus.Completed"/>.</summary>
        public PdfSignatureValidationVerdict? Verdict { get; set; }
    }

    /// <summary>
    /// What EU DSS concluded about one file's signatures. Stored as-is and returned as-is by the
    /// status endpoint, so the stored shape is the API shape.
    /// </summary>
    /// <remarks>
    /// Levels, indications and origins are kept as the strings the validator reports
    /// (<c>PAdES-BASELINE-LT</c>, <c>TOTAL_PASSED</c>, <c>DssDictionary</c>) rather than enums:
    /// DSS owns that vocabulary, and a value it adds later should reach the caller rather than
    /// fail deserialization of an otherwise valid verdict.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class PdfSignatureValidationVerdict
    {
        /// <summary>Signatures found. Zero is a valid, completed verdict.</summary>
        public int SignatureCount { get; set; }

        /// <summary>The weakest level across the signatures; null when there are none.</summary>
        public string? LowestLevel { get; set; }

        /// <summary>Every signature's indication is <c>TOTAL_PASSED</c>; false when there are none.</summary>
        public bool AllPassed { get; set; }

        public DateTime ValidationTime { get; set; }

        /// <summary>When the trusted lists used were loaded, so a caller can see how fresh the trust was.</summary>
        public DateTime? TrustedListsLoadedAt { get; set; }

        /// <summary>One entry per signature, in document order.</summary>
        public IReadOnlyList<PdfSignatureVerdict> Signatures { get; init; } = [];
    }

    /// <summary>One signature's result. See the spec's Domain Model for each field's meaning.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class PdfSignatureVerdict
    {
        public string? FieldName { get; set; }

        public string? Indication { get; set; }

        public string? SubIndication { get; set; }

        public string? SignatureLevel { get; set; }

        public string? SignerSubject { get; set; }

        public DateTime? ClaimedSigningTime { get; set; }

        public DateTime? BestSignatureTime { get; set; }

        /// <summary><c>DssDictionary</c>, <c>Cms</c> or <c>None</c>.</summary>
        public string? RevocationOrigin { get; set; }

        public bool CoversWholeDocument { get; set; }
    }
}
