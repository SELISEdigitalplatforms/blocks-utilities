using Blocks.Genesis;

namespace Sms.DomainService.Requests;

public class SendSmsRequest : IProjectKey
{
    public string? ProjectKey { get; set; }
    public string[] DestinationNumbers { get; set; } = [];
    public string MessageText { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
}
