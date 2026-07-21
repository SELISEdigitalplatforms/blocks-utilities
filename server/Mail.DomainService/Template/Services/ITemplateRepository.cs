using Mail.DomainService.Entities;
using Mail.DomainService.Mails;

namespace Mail.DomainService.Template
{
    public interface ITemplateRepository
    {
        public Task SaveAsync(EmailTemplate template);
        public Task<EmailTemplate> GetByIdAsync(string itemId);
        public Task<GetAllTemplatesResponse> GetsAsync(GetAllTemplates request);
        public Task<EmailTemplate> GetByNameAndLanguageAsync(string name, string language);
        public Task DeleteAsync(string itemId);
        Task<TemplatePluginConfig> GetPluginConfigAsync(string pluginProvider);
    }
}
