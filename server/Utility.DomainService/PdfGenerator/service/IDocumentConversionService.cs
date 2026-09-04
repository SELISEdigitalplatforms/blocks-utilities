namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Accepts document-to-PDF conversions and reports what became of them.
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
        /// Records and queues a conversion. Returns as soon as the work is accepted, not when it is
        /// done.
        /// </summary>
        Task<DocumentConversionResult<ConvertDocumentToPdfAcceptedResponse>> RequestConversionAsync(
            ConvertDocumentToPdfRequest request,
            string correlationId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads a conversion's current state, including a download URL once it has succeeded.
        /// </summary>
        Task<DocumentConversionResult<DocumentConversionStatusResponse>> GetStatusAsync(
            string conversionId,
            string correlationId,
            CancellationToken cancellationToken = default);
    }
}
