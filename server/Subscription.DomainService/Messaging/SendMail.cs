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
}
