using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;

namespace Mail.DomainService.Template.Services
{
    public interface ITemplateService
    {
        public Task<BaseMutationResponse> SaveTemplateAsync(Template template);
        public Task<GetAllTemplatesResponse> GetAllTemplatesAsync(GetAllTemplates request);
        public Task<EmailTemplate?> GetAsync(GetTemplate request);
        public Task<BaseMutationResponse> CloneTemplateAsync(CloneTemplateRequest request);
        public Task<BaseMutationResponse> DeleteAsync(DeleteTemplateRequest request);
    }
}
