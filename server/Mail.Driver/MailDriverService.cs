using Blocks.Genesis;

namespace Blocks.MailDriver;

public class MailDriverService : IMailDriverService
{
    private readonly Mail.DomainService.Mails.IMailService _mailService;
    private readonly Mail.DomainService.Template.Services.ITemplateService _templateService;
    public MailDriverService(Mail.DomainService.Mails.IMailService mailService, Mail.DomainService.Template.Services.ITemplateService templateService)
    {
        _mailService = mailService;
        _templateService = templateService;
    }
    public async Task<BaseMutationResponse> SendAsync(SendMail request)
    {
        var mailRequest = new Mail.DomainService.Mails.SendMail
        {
            ProjectKey = request.ProjectKey,
            SubjectDataContext = request.SubjectDataContext,
            To = request.To,
            Bcc = request.Bcc,
            Cc = request.Cc,
            Purpose = request.Purpose,
            Language = request.Language,
            ReplyTo = request.ReplyTo,
            Attachments = request.Attachments,
            BodyDataContext = request.BodyDataContext,
            SendPhoneNumberAsEmail = request.SendPhoneNumberAsEmail
        };
        return await _mailService.ProcessMailAsync(mailRequest);
    }

    public async Task<BaseMutationResponse> SendToAnyAsync(SendMailToAny request)
    {
        var mailRequest = new Mail.DomainService.Mails.SendMailToAny
        {
            ProjectKey = request.ProjectKey,
            SubjectDataContext = request.SubjectDataContext,
            To = request.To,
            Bcc = request.Bcc,
            Cc = request.Cc,
            Purpose = request.Purpose,
            Language = request.Language,
            ReplyTo = request.ReplyTo,
            Attachments = request.Attachments,
            BodyDataContext = request.BodyDataContext,
            IsTestMail = request.IsTestMail
        };
        return await _mailService.ProcessMailToAnyAsync(mailRequest);
    }

    public async Task<GetAllTemplatesResponse> GetAllTemplatesAsync(GetAllTemplates request)
    {
        var domainRequest = new Mail.DomainService.Mails.GetAllTemplates
        {
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            ProjectKey = request.ProjectKey,
            SearchKey = request.SearchKey,
            SortProperty = request.SortProperty,
            IsDescending = request.IsDescending,
            MailConfigurationId = request.MailConfigurationId,
            Language = request.Language
        };
        var result = await _templateService.GetAllTemplatesAsync(domainRequest);
        return new GetAllTemplatesResponse
        {
            TotalCount = result.TotalCount,
            Templates = result.Templates?.Select(t => new EmailTemplate
            {
                ItemId = t.ItemId,
                Name = t.Name,
                MailConfigurationId = t.MailConfigurationId,
                TemplateBody = t.TemplateBody,
                JsonContent = t.JsonContent,
                ImageId = t.ImageId,
                ImageUrl = t.ImageUrl,
                TemplateSubject = t.TemplateSubject,
                GeneratedBy = t.GeneratedBy,
                Language = t.Language,
                CreatedDate = t.CreatedDate,
                CreatedBy = t.CreatedBy,
                LastUpdatedDate = t.LastUpdatedDate,
                LastUpdatedBy = t.LastUpdatedBy
            }).ToList() ?? []
        };
    }
}
