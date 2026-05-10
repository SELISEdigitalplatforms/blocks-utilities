namespace Blocks.MailDriver;

public class GetAllTemplatesResponse
{
    public int TotalCount { get; set; }
    public List<EmailTemplate> Templates { get; set; } = [];
}
