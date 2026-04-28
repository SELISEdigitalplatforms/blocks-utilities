using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Shared.Services;
using FluentValidation;

namespace CloudConfiguration.DomainService.Notification.Validators
{
    public class NotificationConfigurationValidator : AbstractValidator<SaveNotificatonConfigurationRequest>
    {
        private readonly IConfigurationRepository _configurationRepository;

        public NotificationConfigurationValidator(IConfigurationRepository configurationRepository)
        {
            _configurationRepository = configurationRepository;

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MaximumLength(100).WithMessage("Name must not exceed 100 characters.")
                .MustAsync(IsUniqueNameAsync)
                .When(x => !x.IsUpdateRequest)
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
            var configuration = await _configurationRepository.GetNotificationConfigurationByNameAsync(name);
            return configuration == null;
        }
    }
}
