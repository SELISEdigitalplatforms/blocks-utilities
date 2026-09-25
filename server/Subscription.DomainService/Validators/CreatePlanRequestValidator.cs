using FluentValidation;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Validators;

public sealed class CreatePlanRequestValidator : AbstractValidator<CreatePlanRequest>
{
    private const int MaximumCodeLength = 64;

    public CreatePlanRequestValidator()
    {
        Include(new PlanDefinitionRequestValidator());

        RuleFor(request => request.Code)
            .NotEmpty()
            .MaximumLength(MaximumCodeLength)
            .Matches("^[a-z0-9_-]+$")
            .WithMessage(
                "A plan code may contain only lowercase letters, digits, hyphens and underscores.")
            .WithErrorCode("subscription_plan_code_invalid");

        // Refused at authoring rather than when somebody first tries to seat against it. The
        // person writing the plan is the one who can fix it; an administrator filling seats weeks
        // later can only file a ticket.
        RuleFor(request => request.QuantityItems)
            .Must(ExactlyOneCountsSeats)
            .When(request => request.SubscriberScope == SubscriberScope.User &&
                request.QuantityItems.Count > 1)
            .WithMessage(
                "A user-wise plan selling more than one quantity must mark exactly one of them " +
                "as the quantity that counts people.")
            .WithErrorCode("subscription_plan_seat_quantity_ambiguous");
    }

    /// <summary>
    /// Whether exactly one quantity says how many people may hold a seat.
    /// </summary>
    /// <remarks>
    /// Both halves matter. None marked leaves nothing to seat against; two marked is a plan that
    /// sold eight seats and fifty workspaces and claims both count people, and whichever the seat
    /// service picked would be arbitrary.
    /// </remarks>
    private static bool ExactlyOneCountsSeats(List<PlanQuantityItemRequest> items) =>
        items.Count(item => item.CountsSeats) == 1;
}
