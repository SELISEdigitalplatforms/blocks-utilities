using Blocks.Genesis;

namespace Sms.DomainService.Requests;

public class SendSmsByTemplateRequest : IProjectKey
{
    public string? ProjectKey { get; set; }
    public string[] DestinationNumbers { get; set; } = [];
    public string TemplateName { get; set; } = string.Empty;
    public string Language { get; set; } = "en-US";
    public Dictionary<string, string> DataContext { get; set; } = [];
    public string? CorrelationId { get; set; }
}
