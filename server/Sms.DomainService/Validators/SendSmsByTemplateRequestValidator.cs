using FluentValidation;
using Sms.DomainService.Requests;

namespace Sms.DomainService.Validators;

public class SendSmsByTemplateRequestValidator : AbstractValidator<SendSmsByTemplateRequest>
{
    public SendSmsByTemplateRequestValidator()
    {
        RuleFor(x => x.DestinationNumbers)
            .NotNull()
            .Must(x => x.Length > 0)
            .WithMessage("At least one destination number is required.");

        RuleForEach(x => x.DestinationNumbers)
            .NotEmpty()
            .Matches(@"^\+?[0-9]{7,15}$")
            .WithMessage("Destination number must contain 7 to 15 digits and may start with '+'.");

        RuleFor(x => x.TemplateName).NotEmpty();
        RuleFor(x => x.Language).NotEmpty();
        RuleFor(x => x.DataContext).NotNull();
    }
}
