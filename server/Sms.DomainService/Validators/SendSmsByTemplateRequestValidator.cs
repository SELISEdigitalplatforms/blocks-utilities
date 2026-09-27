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

        // Echoed into logs, stored, and published on status events: identifier characters only.
        RuleFor(x => x.CorrelationId)
            .MaximumLength(100)
            .Matches("^[A-Za-z0-9_.-]+$")
            .When(x => !string.IsNullOrEmpty(x.CorrelationId))
            .WithMessage("Correlation id may contain letters, digits, '_', '.' and '-' only, up to 100 characters.");
    }
}
