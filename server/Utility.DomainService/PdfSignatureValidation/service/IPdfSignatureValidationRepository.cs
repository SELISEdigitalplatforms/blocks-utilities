using Utility.DomainService.PdfSignatureValidation.Entities;

namespace Utility.DomainService.PdfSignatureValidation.service
{
    /// <summary>Repository for PDF signature validation job records.</summary>
    public interface IPdfSignatureValidationRepository
    {
        /// <summary>Records a validation request, replacing any earlier one for the same file.</summary>
        Task<bool> SaveJobAsync(PdfSignatureValidationJob job, string? tenantId = null);

        /// <summary>Reads several validation records in one round trip via an $in filter.</summary>
        Task<List<PdfSignatureValidationJob>> GetJobsAsync(IEnumerable<string> fileIds, string? tenantId = null);

        /// <summary>
        /// Writes a job's new state only if its stored <see cref="PdfSignatureValidationJob.RunId"/>
        /// still matches <paramref name="job"/>'s. Returns false when it does not, meaning the file
        /// was re-requested since this run was queued and the write belongs to a superseded run.
        /// </summary>
        Task<bool> UpdateJobIfCurrentRunAsync(PdfSignatureValidationJob job, string? tenantId = null);
    }
}
