using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentFundReturnStrategyResolverTests
{
    private readonly PaymentFundReturnStrategyResolver _resolver = new();

    [Fact]
    public void Captured_amount_uses_refund()
    {
        var payment = Payment(capturedAmount: 100);

        var decision = _resolver.Resolve(payment, 40);

        decision.IsAllowed.Should().BeTrue();
        decision.Operation.Should().Be(
            PaymentFundReturnOperations.Refund);
    }

    [Fact]
    public void Full_uncaptured_amount_uses_reversal()
    {
        var payment = Payment(capturedAmount: 0);

        var decision = _resolver.Resolve(payment, 100);

        decision.IsAllowed.Should().BeTrue();
        decision.Operation.Should().Be(
            PaymentFundReturnOperations.Reversal);
    }

    [Fact]
    public void Partial_uncaptured_amount_requires_capture()
    {
        var payment = Payment(capturedAmount: 0);

        var decision = _resolver.Resolve(payment, 40);

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("payment_not_captured");
    }

    private static PaymentDetail Payment(decimal capturedAmount) =>
        new()
        {
            PreciseAmount = 100,
            CapturedAmount = capturedAmount
        };
}
