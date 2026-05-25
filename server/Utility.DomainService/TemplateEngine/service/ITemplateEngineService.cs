
namespace Utility.DomainService.TemplateEngine.service
{
    /// <summary>
    /// Service interface for template engine operations
    /// </summary>
    public interface ITemplateEngineService
    {
        // Render operations
        Task<RenderWithJsonResponse> RenderWithJsonAsync(RenderWithJsonRequest request);
        Task<RenderWithJsonBulkResponse> RenderWithJsonBulkAsync(RenderWithJsonBulkRequest request);
        
        // Generate operations
        Task<GenerateRenderedFileResponse> GenerateRenderedFileAsync(GenerateRenderedFileRequest request);
        Task<GenerateRenderedFilesBulkResponse> GenerateRenderedFilesBulkAsync(GenerateRenderedFilesBulkRequest request);
        
        // MongoDB Query operations
        Task<CreateFileWithFilteredMongoQueryResponse> CreateFileWithFilteredMongoQueryAsync(CreateFileWithFilteredMongoQueryRequest request);
        Task<CreateFileWithFilteredMongoQueryBulkResponse> CreateFileWithFilteredMongoQueryBulkAsync(CreateFileWithFilteredMongoQueryBulkRequest request);
        Task<CreateMultipleFileWithFilteredMongoQueryResponse> CreateMultipleFileWithFilteredMongoQueryAsync(CreateMultipleFileWithFilteredMongoQueryRequest request);
    }
}



