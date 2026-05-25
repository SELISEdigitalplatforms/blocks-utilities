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
    }
}


