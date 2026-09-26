using Sms.DomainService.Entities;
using Sms.DomainService.Utilities;

namespace Sms.DomainService.Responses;

public class SmsTemplateView
{
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>The keys a send must supply in DataContext.</summary>
    public IReadOnlyList<string> Placeholders { get; set; } = [];
    public DateTime CreatedDate { get; set; }
    public DateTime LastUpdatedDate { get; set; }

    public static SmsTemplateView From(SmsTemplate template) => new()
    {
        ItemId = template.ItemId,
        Name = template.Name,
        Language = template.Language,
        Body = template.Body,
        Placeholders = SmsTemplateRenderer.Placeholders(template.Body),
        CreatedDate = template.CreatedDate,
        LastUpdatedDate = template.LastUpdatedDate
    };
}

public class SmsTemplateResponse
{
    public bool IsSuccess { get; set; }
    public SmsTemplateView? Template { get; set; }
    public Dictionary<string, string> Errors { get; set; } = [];
}

public class SmsTemplateListResponse
{
    public List<SmsTemplateView> Items { get; set; } = [];
    public long TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
