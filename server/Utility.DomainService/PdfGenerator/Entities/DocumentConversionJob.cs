using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Utility.DomainService.PdfGenerator.Entities
{
    /// <summary>
    /// Where one document-to-PDF conversion has got to.
    /// </summary>
    /// <remarks>
    /// Conversion is queued and answered immediately, so the caller is told the work was accepted
    /// long before it is done. The completion notification is how they normally find out it
    /// finished — but a notification can be missed, and a caller with no other way to ask is left
    /// guessing. This record is that other way: it is written when the request is accepted and
    /// updated as the worker moves through it, so the status endpoint always has something truthful
    /// to answer with.
    /// </remarks>
    [BsonIgnoreExtraElements]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class DocumentConversionJob
    {
        /// <summary>
        /// The conversion's own ID, returned to the caller and used to poll for status. Server
        /// generated: the caller's correlation ID is optional and need not be unique, so it cannot
        /// be the key a status lookup depends on.
        /// </summary>
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The document being converted. Also the ID the PDF is written back to, since conversion
        /// replaces the source file.
        /// </summary>
        public string InputFileId { get; set; } = string.Empty;

        /// <summary>
        /// The caller's own correlation ID, echoed back and used to address the completion
        /// notification. Optional, and not unique.
        /// </summary>
        public string? MessageCoRelationId { get; set; }

        public DocumentConversionStatus Status { get; set; } = DocumentConversionStatus.Queued;

        /// <summary>
        /// The document's name before conversion, recorded once the worker has read the storage
        /// record. Null until then.
        /// </summary>
        public string? SourceFileName { get; set; }

        /// <summary>
        /// The name the file carries after conversion. Null until the conversion succeeds.
        /// </summary>
        public string? ConvertedFileName { get; set; }

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
