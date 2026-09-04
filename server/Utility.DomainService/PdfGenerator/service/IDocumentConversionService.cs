namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Accepts batches of document-to-PDF conversions and reports what became of them.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IPdfGeneratorService"/>, which is a thin queue dispatcher whose
    /// methods all return "queued successfully" and keep no record. Conversion has to be answerable
    /// after the fact — that is the whole point of the status endpoint — so it needs a service that
    /// writes state, not one that only publishes.
    /// </remarks>
    public interface IDocumentConversionService
    {
        /// <summary>
        /// Records and queues a batch of conversions. Returns as soon as the batch is accepted, not
        /// when any conversion is done. Each file in the batch succeeds or fails independently — one
        /// bad ID does not stop the rest of the batch.
        /// </summary>
        Task<DocumentConversionResult<ConvertDocumentsToPdfBatchResponse>> RequestConversionsAsync(
            ConvertDocumentToPdfRequest request,
            string correlationId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the current state of a batch of files, including a download URL for each one that
        /// has succeeded.
        /// </summary>
        Task<DocumentConversionResult<DocumentConversionStatusBatchResponse>> GetStatusAsync(
            GetDocumentConversionStatusRequest request,
            string correlationId,
            CancellationToken cancellationToken = default);
    }
}
