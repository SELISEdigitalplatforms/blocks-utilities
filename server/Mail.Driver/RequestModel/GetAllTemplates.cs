namespace Blocks.MailDriver;

public class GetAllTemplates
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public string ProjectKey { get; set; } = string.Empty;
    public string? SearchKey { get; set; }
    public string? SortProperty { get; set; }
    public bool IsDescending { get; set; }
    public string? MailConfigurationId { get; set; }
    public string? Language { get; set; }
}
