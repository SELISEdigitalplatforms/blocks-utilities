using FluentValidation;
using DomainService.Configuration;
using DomainService.Configuration.Services;

public class ConfigurationValidator : AbstractValidator<SaveConfigurationRequest>
{
    private readonly IConfigurationRepository _configurationRepository;

    public ConfigurationValidator(IConfigurationRepository configurationRepository)
    {
        _configurationRepository = configurationRepository;

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters.")
            .MustAsync(IsUniqueNameAsync)
            .When(x=> !x.IsUpdateRequest)
            .WithMessage("Name must be unique");

        RuleFor(x => x.ChannelToNotify)
            .IsInEnum().WithMessage("Invalid channel type.");

        RuleFor(x => x.NotificationType)
            .IsInEnum().WithMessage("Invalid notification type.");

        RuleFor(x => x.NotifyMethod)
            .NotEmpty().WithMessage("NotifyMethod is required.")
            .MaximumLength(50).WithMessage("NotifyMethod must not exceed 50 characters.");

        RuleFor(x => x.EnablePersistence)
            .NotNull().WithMessage("EnablePersistence must be specified.");
    }

    private async Task<bool> IsUniqueNameAsync(string name, CancellationToken cancellationToken)
    {
        var configuration = await _configurationRepository.GetByNameAsync(name);
        return configuration == null;
    }
}
