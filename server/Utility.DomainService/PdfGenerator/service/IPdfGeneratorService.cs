namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Service interface for PDF generator operations
    /// </summary>
    public interface IPdfGeneratorService
    {
        // Merge operations
        Task<MergePdfsResponse> MergePdfsAsync(MergePdfsRequest request);
        
        // Create PDF from HTML operations
        Task<CreatePdfsFromHtmlResponse> CreatePdfsFromHtmlAsync(CreatePdfsFromHtmlRequest request);
        
        // Extract text operations
        Task<ExtractTextFromPdfsResponse> ExtractTextFromPdfsAsync(ExtractTextFromPdfsRequest request);
        
        // Template engine operations
        Task<CreatePdfsFromHtmlUsingTEResponse> CreatePdfsFromHtmlUsingTEAsync(CreatePdfsFromHtmlUsingTERequest request);
        Task<CreatePdfsFromHtmlUsingTEBulkResponse> CreatePdfsFromHtmlUsingTEBulkAsync(CreatePdfsFromHtmlUsingTEBulkRequest request);
        
        // Fix PDF operations
        Task<FixPdfsResponse> FixPdfsAsync(FixPdfsRequest request);
        
        // Stamp operations
        Task<StampImageToPdfResponse> StampImageToPdfAsync(StampImageToPdfRequest request);
        Task<StampTextToPdfResponse> StampTextToPdfAsync(StampTextToPdfRequest request);
        Task<StampIntoPdfResponse> StampIntoPdfAsync(StampIntoPdfRequest request);
    }
}


