namespace Blocks.MailDriver;

public class SendMail
{
    public string? ProjectKey { get; set; }
    public Dictionary<string, string> SubjectDataContext { get; set; } = [];
    public IEnumerable<string> To { get; set; }
    public IEnumerable<string> Bcc { get; set; } = [];
    public IEnumerable<string> Cc { get; set; } = [];
    public string Purpose { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public IEnumerable<string> ReplyTo { get; set; } = [];
    public IEnumerable<string> Attachments { get; set; } = [];
    public Dictionary<string, string> BodyDataContext { get; set; } = [];
    public bool SendPhoneNumberAsEmail { get; set; } = false;
}
