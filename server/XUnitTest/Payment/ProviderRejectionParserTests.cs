using FluentAssertions;
using Payment.DomainService.Providers.HostedCheckout;

namespace XUnitTest.Payment;

public sealed class ProviderRejectionParserTests
{
    private const string AdyenAcquirerError =
        "Error: {\"status\":500,\"errorCode\":\"905_1\",\"errorType\":\"configuration\"}";

    [Fact]
    public void A_configuration_error_is_recognised_whatever_its_http_status()
    {
        ProviderRejectionParser.TryGetConfigurationErrorCode(AdyenAcquirerError, out var code)
            .Should().BeTrue();
        code.Should().Be("905_1");
    }

    [Theory]
    [InlineData("{\"status\":500,\"errorCode\":\"905_1\",\"errorType\":\"internal\"}")]
    [InlineData("{\"status\":200,\"errorCode\":\"905_1\",\"errorType\":\"configuration\"}")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void Anything_else_is_not(string error) =>
        ProviderRejectionParser.TryGetConfigurationErrorCode(error, out _)
            .Should().BeFalse();

    [Fact]
    public void A_configuration_error_with_no_code_is_still_a_rejection_with_the_generic_code()
    {
        ProviderRejectionParser.TryGetConfigurationErrorCode(
            "{\"status\":500,\"errorType\":\"configuration\"}", out var code).Should().BeTrue();
        code.Should().Be("payment_provider_rejected");
    }

    [Fact]
    public void The_validation_reader_still_refuses_a_server_error()
    {
        // The reason the two readers are separate: widening the 4xx one would change every
        // other caller's reading of a 5xx.
        ProviderRejectionParser.TryGetValidationErrorCode(
            "{\"status\":500,\"errorCode\":\"x\",\"errorType\":\"validation\"}", out _)
            .Should().BeFalse();
    }
}
