using DomainService.Shared;
using FluentValidation;

namespace DomainService.Notification
{
    public class AddSubscriptionRequestValidator : AbstractValidator<Subscription>
    {
        public AddSubscriptionRequestValidator()
        {
           RuleFor(p => p.Payload).NotNull();
           RuleFor(p => p.Payload.ConnectionId).NotEmpty();
           RuleFor(p => p.Payload.SubscriptionFilters).NotEmpty();
          // RuleForEach(p => p.Payload.SubscriptionFilters).SetValidator(validator);
        }
    }
}
