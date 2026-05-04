using DomainService.Configuration.Services;
using DomainService.Entities;
using DomainService.Shared;
using FluentValidation;

namespace DomainService.Notification
{
    public class NotifyRequestValidator : AbstractValidator<NotifyRequest>
    {
        private readonly IConfigurationRepository _configurationRepository;
        private static NotificationConfiguration? _config;

        public NotifyRequestValidator(IConfigurationRepository configurationRepository)
        {
            _configurationRepository = configurationRepository;

            RuleFor(p => p.ConfiguratoinName)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .NotEmpty()
                .MustAsync(IsConfigurationExist).WithMessage("no_configuration_exist");

            RuleFor(p => p.SubscriptionFilters)
                   .Cascade(CascadeMode.Stop)
                   .NotNull()
                   .When(p => _config?.NotificationType == NotificationReceiverTypes.FilterSpecificReceiverType);

            RuleFor(p => p)
                .Cascade(CascadeMode.Stop)
                .Must(UserOrRoleShouldExist)
                .When(p => _config?.NotificationType == NotificationReceiverTypes.UserSpecificReceiverType).WithMessage("UserIds or Roles cannot be empty");
        }

        private async Task<bool> IsConfigurationExist(string configurationName, CancellationToken cancellationToken)
        {
            _config = await _configurationRepository.GetByNameAsync(configurationName);
            return _config != null;
        }

        private bool UserOrRoleShouldExist(NotifyRequest notifyRequest)
        {
            return notifyRequest.UserIds?.Count > 0 || notifyRequest.Roles != null;
        }
    }
}
