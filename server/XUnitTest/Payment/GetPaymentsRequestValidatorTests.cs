using FluentAssertions;
using Payment.DomainService.Requests;
using Payment.DomainService.Validators;

namespace XUnitTest.Payment;

public sealed class GetPaymentsRequestValidatorTests
{
    private readonly GetPaymentsRequestValidator _validator = new();

    [Fact]
    public async Task Default_request_is_valid()
    {
        var result = await _validator.ValidateAsync(
            new GetPaymentsRequest());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task After_and_before_cannot_be_used_together()
    {
        var result = await _validator.ValidateAsync(
            new GetPaymentsRequest
            {
                After = "after-cursor",
                Before = "before-cursor"
            });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(GetPaymentsRequest.After));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Page_size_must_be_between_one_and_one_hundred(
        int pageSize)
    {
        var result = await _validator.ValidateAsync(
            new GetPaymentsRequest
            {
                PageSize = pageSize
            });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_ranges_and_known_values_are_rejected()
    {
        var result = await _validator.ValidateAsync(
            new GetPaymentsRequest
            {
                MinAmount = 20,
                MaxAmount = 10,
                PaymentDateFromUtc = DateTimeOffset.UtcNow,
                PaymentDateToUtc = DateTimeOffset.UtcNow.AddDays(-1),
                PaymentStatuses = ["NOT-A-STATUS"],
                PaymentFlow = "NOT-A-FLOW",
                CurrencyCode = "EURO"
            });

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.PropertyName).Should().Contain(
        [
            nameof(GetPaymentsRequest.MaxAmount),
            nameof(GetPaymentsRequest.PaymentDateToUtc),
            $"{nameof(GetPaymentsRequest.PaymentStatuses)}[0]",
            nameof(GetPaymentsRequest.PaymentFlow),
            nameof(GetPaymentsRequest.CurrencyCode)
        ]);
    }
}
