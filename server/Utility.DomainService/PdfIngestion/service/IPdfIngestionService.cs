namespace Utility.DomainService.PdfIngestion.service
{
    /// <summary>
    /// Accepts batches of PDF ingestions and reports what became of them.
    /// </summary>
    public interface IPdfIngestionService
    {
        /// <summary>
        /// Records and queues a batch of ingestions. Returns as soon as the batch is accepted, not
        /// when any file has actually been inspected. Each file in the batch succeeds or fails
        /// independently - one bad ID does not stop the rest of the batch.
        /// </summary>
        Task<PdfIngestionResult<IngestPdfsBatchResponse>> RequestIngestionsAsync(
            IngestPdfsRequest request,
            string correlationId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the current state of a batch of files, including each one's verdict once its
        /// ingestion has completed.
        /// </summary>
        Task<PdfIngestionResult<PdfIngestionStatusBatchResponse>> GetStatusAsync(
            GetPdfIngestionStatusRequest request,
            string correlationId,
            CancellationToken cancellationToken = default);
    }
}
