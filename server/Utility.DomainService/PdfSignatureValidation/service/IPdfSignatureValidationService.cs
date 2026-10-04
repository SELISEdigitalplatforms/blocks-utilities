using Utility.DomainService.PdfIngestion;

namespace Utility.DomainService.PdfSignatureValidation.service
{
    /// <summary>
    /// Accepts batches of PDF signature validations and reports what became of them.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="PdfIngestionResult{TValue}"/> rather than a type of its own. Validation is
    /// shaped by the spec "as ingestion" - the same batch rules, the same polling contract, the same
    /// status codes - so sharing its result type is what keeps the two from drifting apart, where a
    /// third copy of the same shape would invite exactly that.
    /// </remarks>
    public interface IPdfSignatureValidationService
    {
        /// <summary>
        /// Records and queues a batch of validations, answering once per entry sent. Returns as soon
        /// as the batch is accepted, not when any file has been validated.
        /// </summary>
        Task<PdfIngestionResult<ValidatePdfSignaturesBatchResponse>> RequestValidationsAsync(
            ValidatePdfSignaturesRequest request,
            string correlationId,
            CancellationToken cancellationToken = default);

        /// <summary>Reads the current state of a batch of files, including each verdict once complete.</summary>
        Task<PdfIngestionResult<PdfSignatureValidationStatusBatchResponse>> GetStatusAsync(
            GetPdfSignatureValidationStatusRequest request,
            string correlationId,
            CancellationToken cancellationToken = default);
    }
}
