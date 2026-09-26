using FluentValidation;
using Sms.DomainService.Enums;
using Sms.DomainService.Requests;

namespace Sms.DomainService.Validators;

public class SaveSmsProviderConfigurationRequestValidator : AbstractValidator<SaveSmsProviderConfigurationRequest>
{
    private const string SenderNumberPattern = @"^\+[1-9][0-9]{6,14}$";

    // Carriers' rule for alphanumeric sender ids: 1-11 characters, letters, digits and spaces, and
    // at least one letter so it cannot be mistaken for a short code.
    private const string SenderNamePattern = "^(?=.*[A-Za-z])[A-Za-z0-9 ]{1,11}$";
    private const string TwilioAccountSidPattern = "^AC[0-9a-fA-F]{32}$";

    public SaveSmsProviderConfigurationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ProviderType).IsInEnum();
        RuleFor(x => x.SenderNumber)
            .NotEmpty()
            .When(x => string.IsNullOrWhiteSpace(x.SenderName))
            .WithMessage("A sender number or a sender name is required.");
        RuleFor(x => x.SenderNumber)
            .Matches(SenderNumberPattern)
            .When(x => !string.IsNullOrWhiteSpace(x.SenderNumber))
            .WithMessage("Sender number must be in E.164 format, e.g. +41791234567.");
        RuleFor(x => x.SenderName)
            .Must(name => System.Text.RegularExpressions.Regex.IsMatch(name!.Trim(), SenderNamePattern))
            .When(x => !string.IsNullOrWhiteSpace(x.SenderName))
            .WithMessage("Sender name must be 1 to 11 letters, digits or spaces, with at least one letter.");

        RuleFor(x => x.SenderNameExcludedPrefixes).Must(p => p!.Count <= 50)
            .When(x => x.SenderNameExcludedPrefixes != null)
            .WithMessage("At most 50 excluded country codes.");
        RuleForEach(x => x.SenderNameExcludedPrefixes).NotEmpty().Matches(@"^\s*\+[1-9][0-9]{0,3}\s*$")
            .WithMessage("Excluded prefixes must be country codes such as +1 or +86.");

        RuleFor(x => x.ApiKey).NotEmpty()
            .When(x => string.IsNullOrWhiteSpace(x.ConfigurationId))
            .WithMessage("An API key is required when creating a provider configuration.");
        RuleFor(x => x.ApiKey).MaximumLength(512);

        When(x => x.ProviderType == SmsProviderType.Twilio, () =>
        {
            RuleFor(x => x.AccountId).NotEmpty().Matches(TwilioAccountSidPattern)
                .WithMessage("Twilio account SID must start with 'AC' followed by 32 hex characters.");
        });

        When(x => x.ProviderType == SmsProviderType.Telnyx, () =>
        {
            RuleFor(x => x.MessagingProfileId).NotEmpty().Must(id => Guid.TryParse(id, out _))
                .WithMessage("Telnyx messaging profile id must be a GUID.");
            RuleFor(x => x.WebhookPublicKey).NotEmpty()
                .WithMessage("Telnyx webhook public key is required to verify delivery callbacks.");
        });

        RuleFor(x => x.StatusCallbackBaseUrl)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .When(x => !string.IsNullOrWhiteSpace(x.StatusCallbackBaseUrl))
            .WithMessage("Status callback base URL must be an absolute https URL.");

        RuleFor(x => x.MaxRetryAttempts).InclusiveBetween(1, 10);
        RuleFor(x => x.DeliveryCheckDelayMinutes).InclusiveBetween(1, 1440);

        RuleFor(x => x.RateLimit).NotNull();
        RuleFor(x => x.RateLimit.TenantMaxPerWindow).InclusiveBetween(1, 100_000).When(x => x.RateLimit != null);
        RuleFor(x => x.RateLimit.TenantWindowSeconds).InclusiveBetween(1, 86_400).When(x => x.RateLimit != null);
        RuleFor(x => x.RateLimit.RecipientMaxPerWindow).InclusiveBetween(1, 1_000).When(x => x.RateLimit != null);
        RuleFor(x => x.RateLimit.RecipientWindowSeconds).InclusiveBetween(1, 86_400).When(x => x.RateLimit != null);

        RuleFor(x => x.SpamFilter).NotNull();
        RuleFor(x => x.SpamFilter.MaxRecipients).InclusiveBetween(1, 1_000).When(x => x.SpamFilter != null);
        RuleFor(x => x.SpamFilter.MaxMessageLength).InclusiveBetween(1, 1_600).When(x => x.SpamFilter != null);
        RuleFor(x => x.SpamFilter.UrlPolicy).IsInEnum().When(x => x.SpamFilter != null);
        RuleFor(x => x.SpamFilter.BlockedTerms).NotNull().Must(terms => terms.Count <= 100)
            .When(x => x.SpamFilter != null);
        RuleForEach(x => x.SpamFilter.BlockedTerms).NotEmpty().MaximumLength(50)
            .When(x => x.SpamFilter != null);
    }
}
