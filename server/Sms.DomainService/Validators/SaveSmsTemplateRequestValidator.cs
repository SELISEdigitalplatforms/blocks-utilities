using FluentValidation;
using Sms.DomainService.Requests;

namespace Sms.DomainService.Validators;

public class SaveSmsTemplateRequestValidator : AbstractValidator<SaveSmsTemplateRequest>
{
    public SaveSmsTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9_.-]+$")
            .WithMessage("Template name may contain letters, digits, '_', '.' and '-' only.");

        // BCP 47 in the shape the portal offers: "en", "en-US", "de-CH".
        RuleFor(x => x.Language).NotEmpty().Matches("^[a-z]{2,3}(-[A-Z]{2})?$")
            .WithMessage("Language must look like 'en' or 'en-US'.");

        // Same ceiling as SendSmsRequest.MessageText: ten concatenated segments.
        RuleFor(x => x.Body).NotEmpty().MaximumLength(1600);
    }
}
