namespace Utility.DomainService.PdfSignatureValidation.Events
{
    /// <summary>
    /// Event for validating the signatures of one PDF already in storage.
    /// </summary>
    /// <remarks>
    /// Carries <see cref="UserId"/> for the same reason <c>IngestPdfEvent</c> does, and
    /// <see cref="RunId"/> so the worker can tell whether the job it is about to write is still the
    /// one this event was queued for.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record ValidatePdfSignaturesEvent
    {
        /// <summary>The file to validate, and the key of the job record the worker updates.</summary>
        public string FileId { get; set; } = string.Empty;

        /// <summary>The job's run ID at the time this event was queued. See <c>PdfSignatureValidationJob.RunId</c>.</summary>
        public string RunId { get; set; } = string.Empty;

        public string? MessageCoRelationId { get; set; }

        public string? ProjectKey { get; set; }

        public string? UserId { get; set; }
    }
}
