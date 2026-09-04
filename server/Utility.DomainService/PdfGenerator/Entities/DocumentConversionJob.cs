using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Utility.DomainService.PdfGenerator.Entities
{
    /// <summary>
    /// Where the conversion of one file has got to.
    /// </summary>
    /// <remarks>
    /// Conversion is queued and answered immediately, so the caller is told the work was accepted
    /// long before it is done. The completion notification is how they normally find out it
    /// finished — but a notification can be missed, and a caller with no other way to ask is left
    /// guessing. This record is that other way: written when the request is accepted and updated as
    /// the worker moves through it, so the status endpoint always has something truthful to answer
    /// with.
    /// <para>
    /// Keyed by the file's own storage ID. Conversion replaces the file in place, so that ID is
    /// stable across the operation and is what the caller already holds — there is no second
    /// identifier for them to keep track of. Converting the same file again replaces this record;
    /// what matters is the state the file is in now, not a history of attempts.
    /// </para>
    /// </remarks>
    [BsonIgnoreExtraElements]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class DocumentConversionJob
    {
        /// <summary>
        /// The file's storage ID, and this record's key.
        /// </summary>
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The caller's own correlation ID, used to address the completion notification. Optional,
        /// and not unique, which is why it is not the key.
        /// </summary>
        public string? MessageCoRelationId { get; set; }

        public DocumentConversionStatus Status { get; set; } = DocumentConversionStatus.Queued;

        /// <summary>
        /// The file's name as it currently stands: the document's name while the conversion is
        /// queued or running, and the .pdf name once it has succeeded. Null until the worker has
        /// read the storage record.
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>
        /// Why the conversion failed, in a form a client can branch on. Null unless
        /// <see cref="Status"/> is <see cref="DocumentConversionStatus.Failed"/>.
        /// </summary>
        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }

        public string TenantId { get; set; } = string.Empty;

        public string? CreatedBy { get; set; }

        public DateTime CreateDate { get; set; }

        public DateTime LastUpdateDate { get; set; }

        /// <summary>
        /// When the conversion reached a terminal state. Null while it is still queued or running.
        /// </summary>
        public DateTime? CompletedDate { get; set; }
    }

    /// <summary>
    /// The states a conversion moves through.
    /// </summary>
    /// <remarks>
    /// Serialized as a string rather than an integer, so a stored record stays readable and adding a
    /// state later cannot renumber the ones already written.
    /// </remarks>
    public enum DocumentConversionStatus
    {
        /// <summary>Accepted and waiting for a worker.</summary>
        Queued,

        /// <summary>A worker has picked it up.</summary>
        Processing,

        /// <summary>The PDF has replaced the source file.</summary>
        Succeeded,

        /// <summary>The conversion will not complete. <c>ErrorCode</c> says why.</summary>
        Failed
    }
}
