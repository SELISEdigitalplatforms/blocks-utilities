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

    [Theory]
    [InlineData("The account has insufficient balance")]
    [InlineData("Amount exceeds the authorised total")]
    public void Insufficient_balance_reasons_are_normalized(string reason)
    {
        var result = _mapper.Map("REFUND", false, reason);

        result.Should().NotBeNull();
        result!.Code.Should().Be("insufficient_provider_balance");
    }

    [Fact]
    public void Expired_authorization_reason_is_normalized()
    {
        var result = _mapper.Map("CAPTURE", false, "The authorization has expired");

        result.Should().NotBeNull();
        result!.Code.Should().Be("payment_authorization_expired");
    }

    [Theory]
    [InlineData("CAPTURE", "provider_capture_rejected")]
    [InlineData("CAPTURE_FAILED", "provider_capture_rejected")]
    [InlineData("REFUND", "provider_fund_return_rejected")]
    [InlineData("REFUND_FAILED", "provider_fund_return_rejected")]
    [InlineData("CANCEL_OR_REFUND", "provider_fund_return_rejected")]
    [InlineData("AUTHORISATION", "provider_payment_operation_rejected")]
    [InlineData(null, "provider_payment_operation_rejected")]
    public void Generic_rejection_code_reflects_event_operation(
        string? eventCode,
        string expectedCode)
    {
        var result = _mapper.Map(eventCode, false, "declined by acquirer");

        result.Should().NotBeNull();
        result!.Code.Should().Be(expectedCode);
    }

    [Fact]
    public void Null_reason_still_produces_a_generic_rejection()
    {
        var result = _mapper.Map("REFUND", false, null);

        result.Should().NotBeNull();
        result!.Code.Should().Be("provider_fund_return_rejected");
    }
}
