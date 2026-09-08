namespace Utility.DomainService.PdfIngestion
{
    /// <summary>
    /// What a PDF-ingestion operation returns, whether or not it worked.
    /// </summary>
    /// <remarks>
    /// The same shape as <c>DocumentConversionResult&lt;TValue&gt;</c>, and deliberately its own type
    /// rather than a reuse of it: <c>PdfIngestion</c> is a sibling feature of <c>PdfGenerator</c>, not
    /// a consumer of it, and should not inherit PdfGenerator's failure vocabulary just because the
    /// shape happens to match.
    /// </remarks>
    public sealed class PdfIngestionResult<TValue>
    {
        private PdfIngestionResult()
        {
        }

        public bool IsSuccess { get; private init; }

        public TValue? Value { get; private init; }

        public PdfIngestionFailureKind FailureKind { get; private init; }

        public string? ErrorCode { get; private init; }

        public string? ErrorMessage { get; private init; }

        public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; private init; }

        public string CorrelationId { get; private init; } = string.Empty;

        public static PdfIngestionResult<TValue> Success(TValue value, string correlationId) =>
            new()
            {
                IsSuccess = true,
                Value = value,
                CorrelationId = correlationId
            };

        public static PdfIngestionResult<TValue> Failure(
            PdfIngestionFailureKind kind,
            string errorCode,
            string errorMessage,
            string correlationId,
            IReadOnlyDictionary<string, string[]>? validationErrors = null) =>
            new()
            {
                IsSuccess = false,
                FailureKind = kind,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                CorrelationId = correlationId,
                ValidationErrors = validationErrors
            };
    }

    /// <summary>The shape of a failure, independent of HTTP. The API layer maps these to status codes.</summary>
    public enum PdfIngestionFailureKind
    {
        /// <summary>The request itself is wrong - a missing or malformed field.</summary>
        Validation,

        /// <summary>The ingestion job or the file being asked about does not exist.</summary>
        NotFound,

        /// <summary>A dependency was unreachable - storage, or the message broker.</summary>
        Unavailable,

        /// <summary>Anything else.</summary>
        Internal
    }
}
