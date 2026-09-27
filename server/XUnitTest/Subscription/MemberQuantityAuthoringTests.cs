using FluentAssertions;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Validators;

namespace XUnitTest.Subscription;

/// <summary>
/// That a plan says which of its quantities counts people, before anyone is seated against it.
/// </summary>
/// <remarks>
/// A plan may sell seats and workspaces and anything else, and nothing on a quantity item tells
/// them apart — <c>ItemKey</c> is a free string a product chose. Left unsaid, the seat service can
/// only refuse, and it refuses at the moment an administrator tries to fill a seat: weeks after
/// authoring, to somebody who cannot fix the plan.
/// <para>
/// Caught here instead, the error reaches the person writing the plan while they are writing it.
/// </para>
/// </remarks>
public sealed class MemberQuantityAuthoringTests
{
    [Fact]
    public void A_user_wise_plan_selling_several_quantities_must_say_which_counts_people()
    {
        var result = new CreatePlanRequestValidator().Validate(
            UserWisePlan(Seats(marked: false), Workspaces(marked: false)));

        result.IsValid.Should().BeFalse(
            because: "unmarked, the plan cannot be seated at all, and the administrator who " +
                     "discovers that is not the one who can fix it");
        result.Errors.Should().Contain(error =>
            error.ErrorCode == "subscription_plan_member_quantity_ambiguous");
    }

    [Fact]
    public void Marking_two_quantities_as_people_is_refused()
    {
        var result = new CreatePlanRequestValidator().Validate(
            UserWisePlan(Seats(marked: true), Workspaces(marked: true)));

        result.IsValid.Should().BeFalse(
            because: "whichever one was chosen would be arbitrary, and one of them sells fifty " +
                     "seats where three were paid for");
    }

    [Fact]
    public void Marking_exactly_one_is_accepted()
    {
        new CreatePlanRequestValidator()
            .Validate(UserWisePlan(Seats(marked: true), Workspaces(marked: false)))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_user_wise_plan_selling_one_quantity_needs_no_mark()
    {
        new CreatePlanRequestValidator()
            .Validate(UserWisePlan(Seats(marked: false)))
            .IsValid.Should().BeTrue(
                because: "there is nothing to disambiguate, and requiring the mark anyway would " +
                         "reject the simplest plan anyone can write");
    }

    [Fact]
    public void An_organization_wise_plan_is_not_asked_the_question()
    {
        var plan = UserWisePlan(Seats(marked: false), Workspaces(marked: false));
        plan.SubscriberScope = SubscriberScope.Organization;

        new CreatePlanRequestValidator().Validate(plan)
            .IsValid.Should().BeTrue(
                because: "it has no seats to give out, so which quantity counts people is a " +
                         "question that does not arise — and asking it would refuse plans that " +
                         "have sold happily for as long as the product has existed");
    }

    private static PlanQuantityItemRequest Seats(bool marked) => new()
    {
        ItemKey = "seat",
        UnitLabel = "seat",
        MinQuantity = 1,
        DefaultQuantity = 1,
        CountsMembers = marked
    };

    private static PlanQuantityItemRequest Workspaces(bool marked) => new()
    {
        ItemKey = "workspace",
        UnitLabel = "workspace",
        MinQuantity = 1,
        DefaultQuantity = 1,
        CountsMembers = marked
    };

    private static CreatePlanRequest UserWisePlan(params PlanQuantityItemRequest[] items) => new()
    {
        Code = "starter",
        DisplayName = "Starter",
        SubscriberScope = SubscriberScope.User,
        UsageInterval = BillingInterval.Month,
        UsageIntervalCount = 1,
        QuantityItems = [.. items]
    };
}
