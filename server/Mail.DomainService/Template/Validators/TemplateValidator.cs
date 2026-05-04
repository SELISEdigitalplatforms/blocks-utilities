using FluentValidation;

namespace Mail.DomainService.Template.Validators
{
    public class TemplateValidator : AbstractValidator<Template>
    {
        private readonly ITemplateRepository _templateRepository;

        public TemplateValidator(ITemplateRepository templateRepository)
        {
            _templateRepository = templateRepository;

            RuleFor(template => template.Name)
            .MustAsync(async (name, cancellationToken) => await IsNameUniqueAsync(name))
            .WithMessage("The name must be unique.");
        }

        private async Task<bool> IsNameUniqueAsync(string name)
        {
            var template = await _templateRepository.GetByIdAsync(name);
            return template == null;
        }
    }
}
