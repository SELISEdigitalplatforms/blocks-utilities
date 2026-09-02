using FluentAssertions;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// The rules a fractional metered quantity obeys, and the one place a fractional charge becomes
/// money.
/// </summary>
public sealed class MeterQuantityTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(-7, 0)]
    [InlineData(0.5, 1)]
    [InlineData(512.5, 1)]
    [InlineData(0.001, 3)]
    [InlineData(0.000001, 6)]
    [InlineData(-0.25, 2)]
    public void The_scale_is_the_number_of_decimal_places(decimal value, int expected) =>
        MeterQuantity.ScaleOf(value).Should().Be(expected);

    /// <summary>
    /// Trailing zeroes do not count.
    /// </summary>
    /// <remarks>
    /// <c>1.50m</c> and <c>1.5m</c> are equal but differently scaled, and a value arriving from
    /// Decimal128 arithmetic or from JSON can carry places nobody typed. Refusing the first on a
    /// one-place meter while accepting the second would be arbitrary.
    /// </remarks>
    [Fact]
    public void Trailing_zeroes_do_not_raise_the_scale()
    {
        MeterQuantity.ScaleOf(1.50m).Should().Be(1);
        MeterQuantity.ScaleOf(1.500000m).Should().Be(1);
        MeterQuantity.ScaleOf(500.000000m).Should().Be(0);
        MeterQuantity.IsWithinScale(500.000000m, 0).Should().BeTrue();
    }

    /// <summary>
    /// Zero is whole units only. This is the case every meter authored before fractions existed
    /// falls into, so it is the one that must not have moved.
    /// </summary>
    [Theory]
    [InlineData(100, true)]
    [InlineData(0, true)]
    [InlineData(-3, true)]
    [InlineData(0.5, false)]
    [InlineData(100.1, false)]
    public void Scale_zero_accepts_only_whole_units(decimal value, bool expected) =>
        MeterQuantity.IsWithinScale(value, 0).Should().Be(expected);

    [Theory]
    [InlineData(512.5, 3, true)]
    [InlineData(512.001, 3, true)]
    [InlineData(512.0001, 3, false)]
    [InlineData(512, 3, true)]
    public void A_declared_scale_accepts_that_many_places_and_no_more(
        decimal value,
        int scale,
        bool expected) =>
        MeterQuantity.IsWithinScale(value, scale).Should().Be(expected);

    /// <summary>A scale above the platform maximum admits nothing, rather than admitting more.</summary>
    [Fact]
    public void A_scale_beyond_the_maximum_is_not_a_scale()
    {
        MeterQuantity.IsValidScale(MeterQuantity.MaxScale).Should().BeTrue();
        MeterQuantity.IsValidScale(MeterQuantity.MaxScale + 1).Should().BeFalse();
        MeterQuantity.IsValidScale(-1).Should().BeFalse();
        MeterQuantity.IsWithinScale(0.5m, MeterQuantity.MaxScale + 1).Should().BeFalse();
        MeterQuantity.IsWithinScale(0.5m, -1).Should().BeFalse();
    }

    [Fact]
    public void Magnitude_is_bounded_in_both_directions()
    {
        MeterQuantity.IsWithinMagnitude(MeterQuantity.MaxMagnitude).Should().BeTrue();
        MeterQuantity.IsWithinMagnitude(-MeterQuantity.MaxMagnitude).Should().BeTrue();
        MeterQuantity.IsWithinMagnitude(MeterQuantity.MaxMagnitude + 1).Should().BeFalse();
        MeterQuantity.IsWithinMagnitude(-MeterQuantity.MaxMagnitude - 1).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, "1")]
    [InlineData(1, "0.1")]
    [InlineData(3, "0.001")]
    [InlineData(6, "0.000001")]
    public void The_smallest_step_is_one_at_the_declared_scale(int scale, string expected) =>
        MeterQuantity.SmallestStep(scale).Should().Be(decimal.Parse(
            expected, System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Half a minor unit rounds up, and does so symmetrically about zero.
    /// </summary>
    /// <remarks>
    /// Away from zero rather than to even, so a reversal of a charge that rounded up reverses the
    /// whole of what was charged. Banker's rounding would leave a minor unit behind on half the
    /// reversals, and the customer would have paid it.
    /// </remarks>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(1.4, 1)]
    [InlineData(1.5, 2)]
    [InlineData(2.5, 3)]
    [InlineData(-1.5, -2)]
    [InlineData(-2.5, -3)]
    [InlineData(0.0001, 0)]
    public void A_fractional_charge_rounds_half_away_from_zero(decimal exact, long expected) =>
        MeterQuantity.ToMinorUnits(exact).Should().Be(expected);

    /// <summary>
    /// An amount no minor-unit figure can hold is refused rather than wrapped.
    /// </summary>
    /// <remarks>
    /// The narrowing cast is checked for the same reason the tier walk raises: a valid rate and a
    /// valid quantity can each pass on their own and still multiply past what a long holds.
    /// Wrapping would misprice the charge instead of refusing it.
    /// </remarks>
    [Fact]
    public void An_unrepresentable_charge_throws_rather_than_wrapping()
    {
        var act = () => MeterQuantity.ToMinorUnits(decimal.MaxValue);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void A_quantity_is_described_without_trailing_zeroes()
    {
        MeterQuantity.Describe(500m).Should().Be("500");
        MeterQuantity.Describe(512.500m).Should().Be("512.5");
        MeterQuantity.Describe(0.000001m).Should().Be("0.000001");
    }
}
