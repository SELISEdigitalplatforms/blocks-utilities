namespace Utility.DomainService.TemplateEngine.service
{
    /// <summary>
    /// Repository interface for template engine data operations
    /// </summary>
    public interface ITemplateEngineRepository
    {
        // Template operations
        Task<HtmlTemplate?> GetTemplateByIdAsync(string templateId);
        Task<bool> SaveTemplateAsync(HtmlTemplate template);
        Task<bool> TemplateExistsAsync(string templateId);
        
        // PDF Generation Query operations
        Task<List<PdfGenerationQuery>> GetPdfGenerationQueriesByDirectoryIdAsync(string directoryId);
        Task<PdfGenerationQuery?> GetPdfGenerationQueryByIdAsync(string itemId);
        
        // User Readable Data operations
        Task<string[]> GetUserReadableFieldsAsync(string entityName);
        
        // Entity operations
        Task<string?> GetEntityByItemIdAsync(string entityName, string itemId);
    }

    /// <summary>
    /// Represents an HTML template
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class HtmlTemplate
    {
        public string ItemId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Represents a PDF generation query configuration
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfGenerationQuery
    {
        public string ItemId { get; set; } = string.Empty;
        public string DirectoryId { get; set; } = string.Empty;
        public string BindedTemplateStorageId { get; set; } = string.Empty;
        public string TemplateFileId { get; set; } = string.Empty;
        public string EntityName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string? OrderBy { get; set; }
        public int? PageLimit { get; set; }
        public int? PageNumber { get; set; }
        public string? MetaData { get; set; }
        public bool? FromGetFilteredComplex { get; set; }
        public bool? IsRoot { get; set; }
    }
}



