using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Utility.DomainService.PdfGenerator.Tooling.Inspection;
using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfIngestion.Entities
{
    /// <summary>
    /// Where the ingestion of one file has got to.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>DocumentConversionJob</c>: ingestion is queued and answered immediately, so this
    /// record is written when the request is accepted and updated as the worker moves through it,
    /// giving the status endpoint something truthful to answer with even if the completion
    /// notification is missed.
    /// <para>
    /// Keyed by the file's own storage ID, for the same reason: remediation replaces the file in
    /// place, so that ID is stable across the operation and is what the caller already holds.
    /// </para>
    /// </remarks>
    [BsonIgnoreExtraElements]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfIngestionJob
    {
        /// <summary>The file's storage ID, and this record's key.</summary>
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;

        /// <summary>The caller's own correlation ID, used to address the completion notification.</summary>
        public string? MessageCoRelationId { get; set; }

        /// <summary>
        /// Serialized as a string, unlike <c>DocumentConversionJob.Status</c> whose doc comment
        /// claims the same but omits the attribute - so a stored record stays readable and adding a
        /// state later cannot renumber the ones already written.
        /// </summary>
        [BsonRepresentation(BsonType.String)]
        public PdfIngestionStatus Status { get; set; } = PdfIngestionStatus.Queued;

        public string? FileName { get; set; }

        /// <summary>
        /// The requesting user, captured at accept time and carried on the queued event rather than
        /// read from ambient <c>BlocksContext</c> when the worker later sends the completion
        /// notification - a queue hop is not guaranteed to preserve that context.
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>Why the job itself failed to produce a verdict. Null unless <see cref="Status"/> is <see cref="PdfIngestionStatus.Failed"/>.</summary>
        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }

        public string TenantId { get; set; } = string.Empty;

        public string? CreatedBy { get; set; }

        public DateTime CreateDate { get; set; }

        public DateTime LastUpdateDate { get; set; }

        /// <summary>When the job reached a terminal state. Null while still queued or running.</summary>
        public DateTime? CompletedDate { get; set; }

        /// <summary>
        /// True when this run asked the pipeline to inspect and report only, uploading nothing back
        /// to storage - the de-risking switch for the first production rollout of an otherwise
        /// destructive in-place feature.
        /// </summary>
        public bool DryRun { get; set; }

        /// <summary>The pipeline's verdict, once <see cref="Status"/> reaches <see cref="PdfIngestionStatus.Completed"/>.</summary>
        public PdfIngestionVerdict? Verdict { get; set; }
    }

    /// <summary>
    /// The stored shape of a pipeline run's outcome.
    /// </summary>
    /// <remarks>
    /// Deliberately not the same type <c>PdfIngestionPipeline</c> returns
    /// (<see cref="PdfIngestionOutcome"/>): that type carries <c>FinalBytes</c>, the file's full
    /// content, which must never end up written into a Mongo document alongside every other job's -
    /// exactly the bloat risk this feature's own risk register calls out for the veraPDF raw report.
    /// <see cref="FromOutcome"/> is the one place that gap is bridged.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class PdfIngestionVerdict
    {
        public int PageCount { get; set; }

        public bool WasReadable { get; set; }
        public bool IsReadable { get; set; }
        public bool RepairedWithQpdf { get; set; }

        public bool GeometryWasReadable { get; set; } = true;
        public bool GeometryIsReadable { get; set; } = true;
        public bool GeometryNormalized { get; set; }
        public List<PdfPageGeometry> PageGeometry { get; set; } = [];
        public string? GeometrySkippedReason { get; set; }

        public bool HasSignature { get; set; }
        public int SignatureCount { get; set; }

        public PdfAMetadataClaim MetadataClaim { get; set; } = PdfAMetadataClaim.Missing;
        public string? ClaimedProfile { get; set; }
        public string? ValidatedProfile { get; set; }
        public bool IsPdfA { get; set; }
        public bool IsCompliant { get; set; }
        public List<string> PdfAFailedChecks { get; set; } = [];
        public bool PdfARepairAttempted { get; set; }
        public bool? PdfARepairSucceeded { get; set; }
        public string? FinalProfile { get; set; }

        public bool ContentChanged { get; set; }
        public List<PdfIngestionStageRecord> Stages { get; set; } = [];

        public static PdfIngestionVerdict FromOutcome(PdfIngestionOutcome outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);

            return new PdfIngestionVerdict
            {
                PageCount = outcome.PageCount,
                WasReadable = outcome.WasReadable,
                IsReadable = outcome.IsReadable,
                RepairedWithQpdf = outcome.RepairedWithQpdf,
                GeometryWasReadable = outcome.GeometryWasReadable,
                GeometryIsReadable = outcome.GeometryIsReadable,
                GeometryNormalized = outcome.GeometryNormalized,
                PageGeometry = [.. outcome.PageGeometry],
                GeometrySkippedReason = outcome.GeometrySkippedReason,
                HasSignature = outcome.HasSignature,
                SignatureCount = outcome.SignatureCount,
                MetadataClaim = outcome.MetadataClaim,
                ClaimedProfile = outcome.ClaimedProfile,
                ValidatedProfile = outcome.ValidatedProfile,
                IsPdfA = outcome.IsPdfA,
                IsCompliant = outcome.IsCompliant,
                PdfAFailedChecks = [.. outcome.PdfAFailedChecks],
                PdfARepairAttempted = outcome.PdfARepairAttempted,
                PdfARepairSucceeded = outcome.PdfARepairSucceeded,
                FinalProfile = outcome.FinalProfile,
                ContentChanged = outcome.ContentChanged,
                Stages = [.. outcome.Stages]
            };
        }
    }
}
