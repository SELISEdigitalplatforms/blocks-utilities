using CloudConfiguration.DomainService.Mail.RequestModel;
using CloudConfiguration.DomainService.Shared.Services;
using FluentValidation;

namespace CloudConfiguration.DomainService.Mail.Validators
{
    public class MailConfigurationValidator : AbstractValidator<MailConfiguration>
    {
        private readonly IConfigurationRepository _configurationRepository;

        public MailConfigurationValidator(IConfigurationRepository configurationRepository)
        {
            _configurationRepository = configurationRepository;

            // ConfigurationName is required and should not be empty
            RuleFor(x => x.ConfigurationName)
                .NotEmpty().WithMessage("Configuration name is required.")
                .MustAsync(async (name, cancellationToken) => await IsNameUniqueAsync(name))
                .WithMessage("The name must be unique.")
                .Length(3, 100).WithMessage("Configuration name must be between 3 and 100 characters.");

            // ConfigurationId is required
            RuleFor(x => x.ConfigurationId)
                .NotEmpty().WithMessage("Configuration ID is required.");

            // Host is required and should not be empty
            RuleFor(x => x.Host)
                .NotEmpty().WithMessage("Host is required.")
                .Matches(@"^([\w\-]+\.)*[\w\-]+\.[a-z]{2,}$").WithMessage("Invalid host format.");

            // Port should be in a valid range (assuming typical mail server port range)
            RuleFor(x => x.Port)
                .InclusiveBetween(1, 65535).WithMessage("Port must be between 1 and 65535.");

            // SenderName should not be empty (Only for Outbound)
            RuleFor(x => x.SenderName)
                .NotEmpty().When(x => !x.IsInbound).WithMessage("Sender name is required.")
                .Length(3, 100).When(x => !x.IsInbound).WithMessage("Sender name must be between 3 and 100 characters.");

            // SenderAddress should be a valid email (Only for Outbound)
            RuleFor(x => x.SenderAddress)
                .NotEmpty().When(x => !x.IsInbound).WithMessage("Sender email address is required.")
                .EmailAddress().When(x => !x.IsInbound).WithMessage("Sender email address must be a valid email.");

            // UserName is required
            RuleFor(x => x.SenderUserName)
                .NotEmpty().WithMessage("Username is required.");

            // Password is required
            RuleFor(x => x.AccountPassword)
                .NotEmpty().WithMessage("Password is required.")
                .MinimumLength(6).WithMessage("Password must be at least 6 characters long.");
        }

        private async Task<bool> IsNameUniqueAsync(string name)
        {
            var configuration = await _configurationRepository.GetMailConfigurationByNameAsync(name);
            return configuration == null;
        }
    }
}
