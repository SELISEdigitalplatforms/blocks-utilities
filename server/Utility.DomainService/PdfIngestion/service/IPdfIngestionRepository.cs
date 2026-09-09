using Utility.DomainService.PdfIngestion.Entities;

namespace Utility.DomainService.PdfIngestion.service
{
    /// <summary>
    /// Repository for PDF ingestion job records.
    /// </summary>
    /// <remarks>
    /// Not appended to <c>IPdfGeneratorRepository</c>, which already carries profiles, extract dumps
    /// and conversion jobs for an unrelated feature - ingestion gets its own repository the same way
    /// it gets its own collection.
    /// </remarks>
    public interface IPdfIngestionRepository
    {
        /// <summary>Records an ingestion, replacing any earlier one for the same file.</summary>
        Task<bool> SaveJobAsync(PdfIngestionJob job, string? tenantId = null);

        /// <summary>
        /// Reads the ingestion state of a file. Null when that file has never been submitted for
        /// ingestion.
        /// </summary>
        Task<PdfIngestionJob?> GetJobAsync(string fileId, string? tenantId = null);

        /// <summary>Reads several ingestion records in one round trip via an $in filter.</summary>
        Task<List<PdfIngestionJob>> GetJobsAsync(IEnumerable<string> fileIds, string? tenantId = null);

        /// <summary>Writes an ingestion's new state.</summary>
        Task<bool> UpdateJobAsync(PdfIngestionJob job, string? tenantId = null);
    }
}
