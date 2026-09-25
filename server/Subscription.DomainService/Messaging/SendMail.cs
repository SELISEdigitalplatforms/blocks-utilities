namespace Subscription.DomainService.Messaging;

/// <summary>
/// Wire contract accepted by the Blocks OS mail listener.
/// </summary>
/// <remarks>
/// Keep property names and shapes compatible with <c>DomainService.Dtos.SendMail</c> in
/// blocks-os. This project intentionally does not take a source dependency on that application.
/// </remarks>
public sealed class SendMail
{
    public Dictionary<string, string> SubjectDataContext { get; set; } = [];

    public IEnumerable<string> To { get; set; } = [];

    public IEnumerable<string> Bcc { get; set; } = [];

    public IEnumerable<string> Cc { get; set; } = [];

    public string Purpose { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public IEnumerable<string> ReplyTo { get; set; } = [];

    public IEnumerable<string> Attachments { get; set; } = [];

    public Dictionary<string, string> BodyDataContext { get; set; } = [];

    /// <summary>
    /// Echoed by the mail service on every line it logs about this mail and on the outcome it
    /// reports back, so both can be matched to the work that sent it.
    /// </summary>
    /// <remarks>
    /// Without it the mail service logged <c>correlationId=null</c>, and a rejected mail could not be
    /// traced to the invoice or warning behind it.
    /// </remarks>
    public string? CorrelationId { get; set; }
}
