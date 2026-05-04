using Mail.DomainService.Entities;

namespace Mail.DomainService.Template
{
    public class GetAllTemplatesResponse
    {
        public int TotalCount { get; set; }
        public List<EmailTemplate> Templates { get; set; }
    }
}
