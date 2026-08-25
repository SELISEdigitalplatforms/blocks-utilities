using FluentAssertions;
using Moq;
using Payment.DomainService.Services;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Validators;

namespace XUnitTest.Subscription;

/// <summary>
/// Which cadences may be sold on the calendar, refused where an author can still fix it.
/// </summary>
/// <remarks>
/// Authoring time is the only place this question has an answer somebody can give. By the time a
/// quarterly price aligned to "the first" reaches a renewal, the choice of which first is a guess,
/// and every guess is a cadence the author did not sell.
/// </remarks>
public sealed class CalendarAlignedPriceValidationTests
{
    private readonly Mock<ICurrencyMinorUnitResolver> _currency = new();

    public CalendarAlignedPriceValidationTests() =>
        _currency
            .Setup(resolver => resolver.TryConvertBack(
                It.IsAny<long>(), It.IsAny<string>(), out It.Ref<decimal>.IsAny))
            .Returns(true);

    [Fact]
    public void A_monthly_price_may_be_aligned_to_the_calendar()
    {
        var result = Validate(BillingAlignment.CalendarMonth, BillingInterval.Month, 1);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(BillingInterval.Month, 3)]
    [InlineData(BillingInterval.Month, 12)]
    [InlineData(BillingInterval.Day, 1)]
    [InlineData(BillingInterval.Week, 2)]
    [InlineData(BillingInterval.Year, 1)]
    public void Every_other_cadence_is_refused_by_error_code(
        BillingInterval interval,
        int intervalCount)
    {
        var result = Validate(BillingAlignment.CalendarMonth, interval, intervalCount);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.ErrorCode == "subscription_billing_alignment_invalid");
    }

    [Theory]
    [InlineData(BillingInterval.Month, 3)]
    [InlineData(BillingInterval.Day, 1)]
    [InlineData(BillingInterval.Year, 1)]
    public void An_anniversary_price_is_never_refused_for_its_alignment(
        BillingInterval interval,
        int intervalCount)
    {
        var result = Validate(BillingAlignment.Anniversary, interval, intervalCount);

        result.IsValid.Should().BeTrue(
            "anniversary is the default, so refusing it here would refuse every existing price");
    }

    /// <summary>
    /// The absent field has to mean anniversary, or every caller that predates alignment starts
    /// failing validation for a question it was never asked.
    /// </summary>
    [Fact]
    public void A_request_that_never_mentions_alignment_is_an_anniversary_price()
    {
        var request = new CreatePriceRequest
        {
            PlanId = "plan-1",
            CurrencyCode = "CHF",
            UnitAmountMinor = 8900,
            Interval = BillingInterval.Year,
            IntervalCount = 1
        };

        request.BillingAlignment.Should().Be(BillingAlignment.Anniversary);
        new CreatePriceRequestValidator(_currency.Object).Validate(request)
            .IsValid.Should().BeTrue();
    }

    private FluentValidation.Results.ValidationResult Validate(
        BillingAlignment alignment,
        BillingInterval interval,
        int intervalCount) =>
        new CreatePriceRequestValidator(_currency.Object).Validate(new CreatePriceRequest
        {
            PlanId = "plan-1",
            CurrencyCode = "CHF",
            UnitAmountMinor = 8900,
            Interval = interval,
            IntervalCount = intervalCount,
            BillingAlignment = alignment
        });
}
