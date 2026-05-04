using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using FluentValidation;

namespace Mail.DomainService.Template.Services
{
    public class TemplateService : ITemplateService
    {
        private readonly IValidator<Template> _validator;
        private readonly ITemplateRepository _templateRepository;

        public TemplateService(IValidator<Template> validator,
                               ITemplateRepository templateRepository)
        {
            _validator = validator;
            _templateRepository = templateRepository;
        }

        public async Task<BaseMutationResponse> SaveTemplateAsync(Template template)
        {
            var validationResult = await _validator.ValidateAsync(template);

            if (!validationResult.IsValid)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                };
            }

            var repoTemplate = await MappedIntoRepoTemplateAsync(template);
            if (repoTemplate == null)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Template", "Template with the same Name and Language already exists" } }
                };
            }

            await _templateRepository.SaveAsync(repoTemplate);

            return new BaseMutationResponse { IsSuccess = true, ItemId = repoTemplate.ItemId };
        }

        private async Task<EmailTemplate?> MappedIntoRepoTemplateAsync(Template template)
        {
            EmailTemplate? existingTemplateWithNameLanguage = null;
            if (!string.IsNullOrWhiteSpace(template.Name) && !string.IsNullOrWhiteSpace(template.Language))
            {
                existingTemplateWithNameLanguage = await _templateRepository.GetByNameAndLanguageAsync(template.Name, template.Language);
            }
            if (string.IsNullOrWhiteSpace(template.ItemId))
            {
                if (existingTemplateWithNameLanguage != null)
                {
                    return null;
                }
                return CreateNewEmailTemplate(template);
            }
            if (existingTemplateWithNameLanguage != null && existingTemplateWithNameLanguage.ItemId != template.ItemId)
            {
                return null;
            }

            var repoTemplate = await _templateRepository.GetByIdAsync(template.ItemId) ?? new EmailTemplate { ItemId = template.ItemId };
            UpdateExistingTemplate(repoTemplate, template);

            repoTemplate.LastUpdatedDate = DateTime.UtcNow;
            repoTemplate.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? "no_user";

            return repoTemplate;
        }

        private EmailTemplate CreateNewEmailTemplate(Template template)
        {
            return new EmailTemplate
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.UtcNow,
                CreatedBy = BlocksContext.GetContext()?.UserId ?? "no_user",
                GeneratedBy = BlocksContext.GetContext()?.UserId ?? "no_user",
                Name = template.Name,
                MailConfigurationId = template.MailConfigurationId,
                ImageId = template.ImageId,
                ImageUrl = template.ImageUrl,
                JsonContent = template.JsonContent,
                Language = template.Language,
                TemplateBody = template.TemplateBody,
                TemplateSubject = template.TemplateSubject
            };
        }

        private void UpdateExistingTemplate(EmailTemplate repoTemplate, Template template)
        {
            repoTemplate.Name = template.Name ?? repoTemplate.Name;
            repoTemplate.MailConfigurationId = template.MailConfigurationId ?? repoTemplate.MailConfigurationId;
            repoTemplate.ImageId = template.ImageId ?? repoTemplate.ImageId;
            repoTemplate.ImageUrl = template.ImageUrl ?? repoTemplate.ImageUrl;
            repoTemplate.JsonContent = template.JsonContent ?? repoTemplate.JsonContent;
            repoTemplate.Language = template.Language ?? repoTemplate.Language;
            repoTemplate.TemplateBody = template.TemplateBody ?? repoTemplate.TemplateBody;
            repoTemplate.TemplateSubject = template.TemplateSubject ?? repoTemplate.TemplateSubject;
        }


        public async Task<GetAllTemplatesResponse> GetAllTemplatesAsync(GetAllTemplates request)
        {
            return await _templateRepository.GetsAsync(request);
        }

        public async Task<EmailTemplate?> GetAsync(GetTemplate request)
        {
            var emailTemplate = await _templateRepository.GetByIdAsync(request.ItemId);
            return emailTemplate;
        }

        public async Task<BaseMutationResponse> CloneTemplateAsync(CloneTemplateRequest request)
        {
            var repoTemplate = await _templateRepository.GetByIdAsync(request.ItemId);
            if (repoTemplate == null)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Template", "Template not found" } }
                };
            }

            var emailTemplate = new EmailTemplate
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                CreatedBy = BlocksContext.GetContext()?.UserId ?? "no_user",
                GeneratedBy = BlocksContext.GetContext()?.UserId ?? "no_user",
                Name = !string.IsNullOrWhiteSpace(request.Name) ? request.Name : repoTemplate.Name + "_clone",
                MailConfigurationId = !string.IsNullOrWhiteSpace(request.MailConfigurationId) ? request.MailConfigurationId : repoTemplate.MailConfigurationId,
                ImageId = repoTemplate.ImageId,
                ImageUrl = repoTemplate.ImageUrl,
                JsonContent = repoTemplate.JsonContent,
                TemplateBody = repoTemplate.TemplateBody,
                Language = !string.IsNullOrWhiteSpace(request.Language) ? request.Language : repoTemplate.Language,
                TemplateSubject = !string.IsNullOrWhiteSpace(request.TemplateSubject) ? request.TemplateSubject : repoTemplate.TemplateSubject
            };

            await _templateRepository.SaveAsync(emailTemplate);

            return new BaseMutationResponse { IsSuccess = true, ItemId = emailTemplate.ItemId };
        }

        public async Task<BaseMutationResponse> DeleteAsync(DeleteTemplateRequest request)
        {

            var config = await _templateRepository.GetByIdAsync(request.ItemId);
            if (config == null)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "ItemId", "ItemId not found" }
                    }
                };
            }

            await _templateRepository.DeleteAsync(request.ItemId);

            return new BaseMutationResponse { IsSuccess = true };
        }
    }
}
