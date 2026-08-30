using FluentAssertions;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Validators;

namespace XUnitTest.Subscription;

public sealed class CreateDiscountRequestValidatorTests
{
    private readonly CreateDiscountRequestValidator _validator = new();

    [Fact]
    public async Task A_percentage_discount_requires_basis_points()
    {
        var result = await _validator.ValidateAsync(new CreateDiscountRequest
        {
            Code = "launch", DisplayName = "Launch", Kind = DiscountKind.Percent
        });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_fixed_discount_requires_amount_and_currency()
    {
        var result = await _validator.ValidateAsync(new CreateDiscountRequest
        {
            Code = "five-off", DisplayName = "Five off", Kind = DiscountKind.FixedAmount
        });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_scoped_percentage_discount_is_valid()
    {
        var result = await _validator.ValidateAsync(new CreateDiscountRequest
        {
            Code = "launch25", DisplayName = "Launch", Kind = DiscountKind.Percent,
            PercentBasisPoints = 2500, ApplicablePlanCodes = ["professional"]
        });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_discount_cannot_expire_at_or_before_its_start()
    {
        var instant = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await _validator.ValidateAsync(new CreateDiscountRequest
        {
            Code = "launch25", DisplayName = "Launch", Kind = DiscountKind.Percent,
            PercentBasisPoints = 2_500, StartsAtUtc = instant, ExpiresAtUtc = instant
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.ErrorMessage == "The discount must expire after it starts.");
    }
}
