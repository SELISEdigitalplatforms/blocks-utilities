using FluentValidation;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

/// <summary>
/// Nothing of its own: an edit rewrites exactly what a create authored, minus the code and scope
/// it may not touch.
/// </summary>
public sealed class UpdatePlanRequestValidator : AbstractValidator<UpdatePlanRequest>
{
    public UpdatePlanRequestValidator() => Include(new PlanDefinitionRequestValidator());
}
