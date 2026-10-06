using FluentAssertions;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentRecoveryBackoffTests
{
    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    [InlineData(6, 900)]
    [InlineData(50, 900)]
    public void Doubles_from_thirty_seconds_and_stops_at_a_quarter_of_an_hour(int attempts, int seconds) =>
        PaymentRecoveryBackoff.DelayFor(attempts).Should().Be(TimeSpan.FromSeconds(seconds));
}
