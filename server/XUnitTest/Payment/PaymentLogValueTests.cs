using FluentAssertions;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentLogValueTests
{
    [Fact]
    public void Hash_returns_a_stable_short_value_without_exposing_the_identifier()
    {
        const string identifier = "sensitive-tenant-or-payment-identifier";

        var first = PaymentLogValue.Hash(identifier);
        var second = PaymentLogValue.Hash(identifier);

        first.Should().Be(second);
        first.Should().HaveLength(16);
        first.Should().NotContain(identifier);
    }

    [Fact]
    public void Label_removes_unsafe_characters_and_limits_the_length()
    {
        var value = $"AUTHORISATION\r\nsecret={new string('x', 100)}";

        var label = PaymentLogValue.Label(value);

        label.Should().HaveLength(64);
        label.Should().NotContain("\r");
        label.Should().NotContain("\n");
        label.Should().NotContain("=");
    }
}
