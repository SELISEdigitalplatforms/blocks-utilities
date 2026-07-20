using FluentAssertions;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class ProviderFailureReasonMapperTests
{
    private readonly ProviderFailureReasonMapper _mapper = new();

    [Fact]
    public void Not_captured_reason_is_normalized_without_raw_text()
    {
        var result = _mapper.Map(
            "REFUND",
            false,
            "Transaction hasn't been captured, refund not possible");

        result.Should().NotBeNull();
        result!.Code.Should().Be("payment_not_captured");
        result.Summary.Should().NotContain("Transaction");
    }

    [Fact]
    public void Successful_event_does_not_create_failure_details()
    {
        _mapper.Map("REFUND", true, "ignored")
            .Should().BeNull();
    }
}
