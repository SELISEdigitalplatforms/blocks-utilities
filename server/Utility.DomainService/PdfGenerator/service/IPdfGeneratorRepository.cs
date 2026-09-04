using Utility.DomainService.PdfGenerator.Entities;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Repository interface for PDF generator data operations
    /// </summary>
    public interface IPdfGeneratorRepository
    {
        Task<PdfUtilityProfile?> GetPdfUtilityProfileAsync(string profileId, string? tenantId = null);
        Task<bool> SavePdfExtractDumpAsync(PdfExtractDump extractDump, string? tenantId = null);
        Task<bool> PdfExtractDumpExistsAsync(string recordId, string? tenantId = null);
        Task<PdfExtractDump?> GetPdfExtractDumpAsync(string recordId, string? tenantId = null);

        // Document conversion jobs. The status endpoint's only source of truth, so a caller that
        // misses the completion notification can still find out what happened.
        Task<bool> SaveDocumentConversionJobAsync(DocumentConversionJob job, string? tenantId = null);
        Task<DocumentConversionJob?> GetDocumentConversionJobAsync(string fileId, string? tenantId = null);

        /// <summary>
        /// Reads the conversion state of several files in one round trip, for the batch status
        /// endpoint. A file with no record simply has no entry in the result — the caller matches
        /// on <c>Id</c> and treats an absent one as never submitted.
        /// </summary>
        Task<List<DocumentConversionJob>> GetDocumentConversionJobsAsync(IEnumerable<string> fileIds, string? tenantId = null);
        Task<bool> UpdateDocumentConversionJobAsync(DocumentConversionJob job, string? tenantId = null);
    }
}


