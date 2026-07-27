using FluentAssertions;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class CheckoutStatusMapperTests
{
    [Theory]
    [InlineData("COMPLETED", "completed")]
    [InlineData("Refused", "refused")]
    [InlineData("canceled", "canceled")]
    [InlineData("cancelled", "canceled")]
    [InlineData("expired", "expired")]
    [InlineData("PaymentPending", "paymentPending")]
    [InlineData("something-odd", "unknown")]
    public void Normalize_maps_provider_status_to_canonical_value(
        string providerStatus, string expected)
    {
        new CheckoutStatusMapper().Normalize(providerStatus).Should().Be(expected);
    }

    [Theory]
    [InlineData("completed", PaymentRedirectStatuses.Success)]
    [InlineData("refused", PaymentRedirectStatuses.Fail)]
    [InlineData("canceled", PaymentRedirectStatuses.Cancelled)]
    [InlineData("expired", PaymentRedirectStatuses.Fail)]
    [InlineData("paymentPending", PaymentRedirectStatuses.Pending)]
    [InlineData("unknown", PaymentRedirectStatuses.Pending)]
    public void ToRedirectStatus_maps_normalized_status_to_redirect_outcome(
        string normalizedStatus, string expected)
    {
        new CheckoutStatusMapper().ToRedirectStatus(normalizedStatus).Should().Be(expected);
    }
}
