using Blocks.Genesis;

namespace Mail.DomainService.Template
{
    public class CloneTemplateRequest 
    {
        public string ItemId { get; set; }
        public string? MailConfigurationId { get; set; }
        public string? Language { get; set; }
        public string? Name { get; set; }
        public string? TemplateSubject { get; set; }
        
    }
}
