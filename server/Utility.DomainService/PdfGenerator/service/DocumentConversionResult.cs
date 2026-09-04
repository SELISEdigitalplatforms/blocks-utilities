namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// What a document-conversion operation returns, whether or not it worked.
    /// </summary>
    /// <remarks>
    /// Modelled on the subscription module's <c>SubscriptionOperationResult</c>: expected failures
    /// are values, not exceptions. A file ID that does not exist or a document type nobody can read
    /// are ordinary outcomes, and throwing for them turns control flow into stack traces and loses
    /// the error code the caller needs.
    /// <para>
    /// The failure kind is declared here rather than reusing the payment module's, because
    /// Utility.DomainService references no other project and pulling in a business domain to borrow
    /// an enum would be a worse trade than a small closed set of its own. The API mapper translates
    /// it to the same status codes the subscription controllers use.
    /// </para>
    /// </remarks>
    public sealed class DocumentConversionResult<TValue>
    {
        private DocumentConversionResult()
        {
        }

        public bool IsSuccess { get; private init; }

        public TValue? Value { get; private init; }

        public DocumentConversionFailureKind FailureKind { get; private init; }

        public string? ErrorCode { get; private init; }

        public string? ErrorMessage { get; private init; }

        public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; private init; }

        public string CorrelationId { get; private init; } = string.Empty;

        public static DocumentConversionResult<TValue> Success(TValue value, string correlationId) =>
            new()
            {
                IsSuccess = true,
                Value = value,
                CorrelationId = correlationId
            };

        public static DocumentConversionResult<TValue> Failure(
            DocumentConversionFailureKind kind,
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

    /// <summary>
    /// The shape of a failure, independent of HTTP. The API layer maps these to status codes.
    /// </summary>
    public enum DocumentConversionFailureKind
    {
        /// <summary>The request itself is wrong — a missing or malformed field.</summary>
        Validation,

        /// <summary>The conversion or the file being asked about does not exist.</summary>
        NotFound,

        /// <summary>The file exists but is not a document this service can convert.</summary>
        Unsupported,

        /// <summary>A dependency was unreachable — storage, or the message broker.</summary>
        Unavailable,

        /// <summary>Anything else.</summary>
        Internal
    }
}
