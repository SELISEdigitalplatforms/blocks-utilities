using Blocks.Genesis;
using FluentValidation;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Template.Models;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Mail.DomainService.Template.Services
{
    public class TemplateService : ITemplateService
    {
        private readonly IValidator<Template> _validator;
        private readonly ITemplateRepository _templateRepository;
        private readonly ILogger<TemplateService> _logger;
        private readonly IHttpService _httpService;

        public TemplateService(IValidator<Template> validator,
                               ITemplateRepository templateRepository,
                               ILogger<TemplateService> logger,
                               IHttpService httpService)
        {
            _validator = validator;
            _templateRepository = templateRepository;
            _logger = logger;
            _httpService = httpService;
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

        public async Task<BeeLoginResponse?> GetTemplatePluginTokenAsync(
            string provider,
            string uId)
        {
            _logger.LogInformation(
                "GetTemplatePluginTokenAsync: started for provider {Provider}",
                provider);

            try
            {
                if (string.IsNullOrWhiteSpace(uId))
                {
                    throw new ArgumentException("UID cannot be empty.", nameof(uId));
                }

                var pluginConfig =
                    await _templateRepository.GetPluginConfigAsync(provider);

                var headers = pluginConfig.HttpHeders?
                    .Where(header => !header.Key.Equals(
                        "Authorization",
                        StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(
                        header => header.Key,
                        header => header.Value)
                    ?? new Dictionary<string, string>();

                var payload = PreparePayload(pluginConfig, uId);

                var (response, errorMessage) =
                    await _httpService.SendRequest<BeeLoginResponse>(
                        new HttpMethod(pluginConfig.HttpMethod),
                        pluginConfig.RequestUri,
                        payload,
                        pluginConfig.ContentType,
                        headers);

                if (response is null)
                {
                    _logger.LogError(
                        "Template plugin request failed. Provider: {Provider}, Error: {Error}",
                        provider,
                        errorMessage);

                    return null;
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "GetTemplatePluginTokenAsync failed for provider {Provider}",
                    provider);

                return null;
            }
        }

        private static object PreparePayload(
            TemplatePluginConfig pluginConfig,
            string uid)
        {
            if (pluginConfig.ContentType.Equals(
                    "application/x-www-form-urlencoded",
                    StringComparison.OrdinalIgnoreCase))
            {
                var payload =
                    JsonSerializer.Deserialize<Dictionary<string, string>>(
                        pluginConfig.Payload)
                    ?? new Dictionary<string, string>();

                // Always overwrite the configured UID.
                payload["uid"] = uid;

                return payload;
            }

            var jsonPayload =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    pluginConfig.Payload)
                ?? new Dictionary<string, JsonElement>();

            // Always overwrite the configured UID.
            jsonPayload["uid"] = JsonSerializer.SerializeToElement(uid);

            return jsonPayload;
        }
    }
}
