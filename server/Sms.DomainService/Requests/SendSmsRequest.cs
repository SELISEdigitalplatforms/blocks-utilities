namespace Sms.DomainService.Requests;

public class SendSmsRequest
{
    public string[] DestinationNumbers { get; set; } = [];
    public string MessageText { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
}
