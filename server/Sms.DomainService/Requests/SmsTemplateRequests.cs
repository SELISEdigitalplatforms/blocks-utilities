namespace Sms.DomainService.Requests;

public class SaveSmsTemplateRequest
{
    /// <summary>Empty to create; an existing id to update.</summary>
    public string? TemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = "en-US";

    /// <summary>Message text with <c>{{key}}</c> placeholders filled from the send's DataContext.</summary>
    public string Body { get; set; } = string.Empty;
}

public class GetSmsTemplatesRequest
{
    /// <summary>Case-insensitive substring of the name.</summary>
    public string? Search { get; set; }
    public string? Language { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
