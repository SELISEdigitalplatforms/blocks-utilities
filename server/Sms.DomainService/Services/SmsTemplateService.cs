using FluentValidation;
using Sms.DomainService.Entities;
using Sms.DomainService.Repositories;
using Sms.DomainService.Requests;
using Sms.DomainService.Responses;
using Sms.DomainService.Utilities;

namespace Sms.DomainService.Services;

/// <summary>Template management for the caller's tenant. A template is identified by name and language.</summary>
public interface ISmsTemplateService
{
    Task<SmsTemplateResponse> SaveAsync(SaveSmsTemplateRequest request, CancellationToken cancellationToken = default);
    Task<SmsTemplateResponse> GetAsync(string templateId, CancellationToken cancellationToken = default);
    Task<SmsTemplateListResponse> ListAsync(GetSmsTemplatesRequest request, CancellationToken cancellationToken = default);
    Task<SmsMutationResponse> DeleteAsync(string templateId, CancellationToken cancellationToken = default);
}

public sealed class SmsTemplateService : ISmsTemplateService
{
    private const int MaxPageSize = 100;

    private readonly IValidator<SaveSmsTemplateRequest> _validator;
    private readonly ISmsRepository _repository;

    public SmsTemplateService(IValidator<SaveSmsTemplateRequest> validator, ISmsRepository repository)
    {
        _validator = validator;
        _repository = repository;
    }

    public async Task<SmsTemplateResponse> SaveAsync(SaveSmsTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return new SmsTemplateResponse
            {
                Errors = validation.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.First().ErrorMessage)
            };
        }

        if (SmsTenantContext.CurrentTenantId() is not { } tenantId)
        {
            return Failure("Tenant", "The request has no tenant context.");
        }

        SmsTemplate template;
        if (string.IsNullOrWhiteSpace(request.TemplateId))
        {
            template = new SmsTemplate { TenantId = tenantId };
        }
        else
        {
            var existing = await _repository.GetTemplateByIdAsync(tenantId, request.TemplateId, cancellationToken);
            if (existing == null)
            {
                return Failure("TemplateId", "SMS template was not found.");
            }

            template = existing;
        }

        // Name + language is what SendByTemplate looks up, so it has to stay unambiguous.
        // ponytail: checked here, not by a unique index, so two simultaneous saves can race; add
        // the index per tenant database if templates are ever created in bulk.
        var clash = await _repository.GetTemplateAsync(tenantId, request.Name, request.Language, cancellationToken);
        if (clash != null && clash.ItemId != template.ItemId)
        {
            return Failure("Name", $"A template named '{request.Name}' already exists for language '{request.Language}'.");
        }

        template.Name = request.Name;
        template.Language = request.Language;
        template.Body = request.Body;
        await _repository.SaveTemplateAsync(template, cancellationToken);

        return new SmsTemplateResponse { IsSuccess = true, Template = SmsTemplateView.From(template) };
    }

    public async Task<SmsTemplateResponse> GetAsync(string templateId, CancellationToken cancellationToken = default)
    {
        var template = SmsTenantContext.CurrentTenantId() is { } tenantId && !string.IsNullOrWhiteSpace(templateId)
            ? await _repository.GetTemplateByIdAsync(tenantId, templateId, cancellationToken)
            : null;

        return template == null
            ? Failure("TemplateId", "SMS template was not found.")
            : new SmsTemplateResponse { IsSuccess = true, Template = SmsTemplateView.From(template) };
    }

    public async Task<SmsTemplateListResponse> ListAsync(GetSmsTemplatesRequest request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var response = new SmsTemplateListResponse { Page = page, PageSize = pageSize };

        if (SmsTenantContext.CurrentTenantId() is not { } tenantId)
        {
            return response;
        }

        var (items, total) = await _repository.ListTemplatesAsync(tenantId, request.Search, request.Language, (page - 1) * pageSize, pageSize, cancellationToken);
        response.Items = items.Select(SmsTemplateView.From).ToList();
        response.TotalCount = total;
        return response;
    }

    public async Task<SmsMutationResponse> DeleteAsync(string templateId, CancellationToken cancellationToken = default)
    {
        var deleted = SmsTenantContext.CurrentTenantId() is { } tenantId && !string.IsNullOrWhiteSpace(templateId) &&
                      await _repository.DeleteTemplateAsync(tenantId, templateId, cancellationToken);

        return deleted
            ? SmsMutationResponse.Success(templateId)
            : SmsMutationResponse.Failure("TemplateId", "SMS template was not found.");
    }

    private static SmsTemplateResponse Failure(string field, string message) =>
        new() { Errors = new Dictionary<string, string> { [field] = message } };
}
