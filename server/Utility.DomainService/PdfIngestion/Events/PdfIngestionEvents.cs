namespace Utility.DomainService.PdfIngestion.Events
{
    /// <summary>
    /// Event for ingesting one PDF: inspecting it and remediating whatever the inspection calls for.
    /// </summary>
    /// <remarks>
    /// Unlike <c>ConvertDocumentToPdfEvent</c>, this carries <see cref="UserId"/> explicitly rather
    /// than leaving the completion notification to read it from ambient <c>BlocksContext</c> at
    /// consume time. A queue hop is not guaranteed to preserve that context, so a value captured at
    /// accept time - when it is known to be correct - is carried on the event instead.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record IngestPdfEvent
    {
        /// <summary>
        /// The file to ingest, and the key of the ingestion record the worker updates as it goes -
        /// the record the status endpoint reads.
        /// </summary>
        public string FileId { get; set; } = string.Empty;

        public string? MessageCoRelationId { get; set; }

        public string? ProjectKey { get; set; }

        public string? UserId { get; set; }

        /// <summary>See <c>PdfIngestionJob.DryRun</c>.</summary>
        public bool DryRun { get; set; }
    }
}
