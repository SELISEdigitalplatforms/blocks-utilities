using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis;
using Utility.DomainService.TemplateEngine.service;
using Utility.DomainService.TemplateEngine;

namespace Api.Controllers
{
    /// <summary>
    /// API Controller for managing template engine operations
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class TemplateEngineController : ControllerBase
    {
        private readonly ITemplateEngineService _templateEngineService;

        /// <summary>
        /// Initializes a new instance of the <see cref="TemplateEngineController"/> class.
        /// </summary>
        /// <param name="templateEngineService">The service used for template engine operations.</param>
        public TemplateEngineController(
            ITemplateEngineService templateEngineService)
        {
            _templateEngineService = templateEngineService;
        }

        /// <summary>
        /// Render a template with JSON data
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Request parameters:
        /// 
        /// TemplateFileId: ID of the template file to render
        /// RenderedFileId: ID for the output rendered file
        /// JSONString: JSON data to be used in rendering
        /// FileNameExtension: Extension for the output file (default: .html)
        /// ProjectKey: Project key for multi-tenancy
        /// SubscriptionFilterId: Filter ID for notifications
        /// NotifyOnProcessEnding: Whether to notify when processing completes
        /// RaiseEventOnProcessEnding: Whether to raise event when processing completes
        /// </remarks>
        /// <returns>RenderWithJsonResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<RenderWithJsonResponse> RenderWithJSON([FromBody] RenderWithJsonRequest request)
        {
            return await _templateEngineService.RenderWithJsonAsync(request);
        }

        /// <summary>
        /// Render multiple templates with JSON data in bulk
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Bulk request with multiple payloads
        /// </remarks>
        /// <returns>RenderWithJsonBulkResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<RenderWithJsonBulkResponse> RenderWithJSONBulk([FromBody] RenderWithJsonBulkRequest request)
        {
            return await _templateEngineService.RenderWithJsonBulkAsync(request);
        }

        /// <summary>
        /// Generate rendered file from template using entity data
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Request parameters:
        /// 
        /// FileId: ID for the output file
        /// TemplateFileId: ID of the template to use
        /// EntityIdentifierList: List of entities to fetch data from
        /// MetaDataList: Additional metadata for rendering
        /// ProjectKey: Project key for multi-tenancy
        /// </remarks>
        /// <returns>GenerateRenderedFileResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<GenerateRenderedFileResponse> GenerateRenderedFile([FromBody] GenerateRenderedFileRequest request)
        {
            return await _templateEngineService.GenerateRenderedFileAsync(request);
        }

        /// <summary>
        /// Generate multiple rendered files in bulk
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Bulk request with multiple file generation requests
        /// </remarks>
        /// <returns>GenerateRenderedFilesBulkResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<GenerateRenderedFilesBulkResponse> GenerateRenderedFileBulk([FromBody] GenerateRenderedFilesBulkRequest request)
        {
            return await _templateEngineService.GenerateRenderedFilesBulkAsync(request);
        }

        /// <summary>
        /// Create file with filtered MongoDB query
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Request parameters:
        /// 
        /// FileId: Output file ID
        /// TemplateFileId: Template to use
        /// FilteredMongoQueryDatas: List of MongoDB queries to fetch entity data
        /// MetaDataList: Additional metadata
        /// ProjectKey: Project key for multi-tenancy
        /// </remarks>
        /// <returns>CreateFileWithFilteredMongoQueryResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<CreateFileWithFilteredMongoQueryResponse> CreateFileWithFilteredMongoQuery([FromBody] CreateFileWithFilteredMongoQueryRequest request)
        {
            return await _templateEngineService.CreateFileWithFilteredMongoQueryAsync(request);
        }

        /// <summary>
        /// Create multiple files with filtered MongoDB queries in bulk
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Bulk request with multiple query data
        /// </remarks>
        /// <returns>CreateFileWithFilteredMongoQueryBulkResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<CreateFileWithFilteredMongoQueryBulkResponse> CreateFileWithFilteredMongoQueryBulk([FromBody] CreateFileWithFilteredMongoQueryBulkRequest request)
        {
            return await _templateEngineService.CreateFileWithFilteredMongoQueryBulkAsync(request);
        }

        /// <summary>
        /// Create multiple files based on saved query configurations
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Request parameters:
        /// 
        /// RequestId: Directory ID containing saved query configurations
        /// TemplateFileId: Optional template ID to override saved configurations
        /// ProjectKey: Project key for multi-tenancy
        /// </remarks>
        /// <returns>CreateMultipleFileWithFilteredMongoQueryResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<CreateMultipleFileWithFilteredMongoQueryResponse> CreateMultipleFileWithFilteredMongoQuery([FromBody] CreateMultipleFileWithFilteredMongoQueryRequest request)
        {
            return await _templateEngineService.CreateMultipleFileWithFilteredMongoQueryAsync(request);
        }
    }
}

