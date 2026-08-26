using FluentAssertions;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// The arithmetic a partial credit note depends on.
/// </summary>
/// <remarks>
/// Every test here is really one property restated: the parts add up to the whole. A credit note whose
/// subtotal and tax do not sum to its total is a document an accountant has to reconcile by hand, and
/// the naive implementation — multiply each part by a fraction, round each one — produces exactly that
/// for the commonest possible input.
/// </remarks>
public sealed class ProportionalAllocationTests
{
    [Fact]
    public void Thirds_of_a_hundred_still_add_up_to_a_hundred()
    {
        // The case that motivates largest remainder. Flooring each third gives 33+33+33 = 99, and the
        // missing minor unit is the discrepancy the whole feature exists to avoid.
        var parts = ProportionalAllocation.Split(100, [1, 1, 1]);

        parts.Sum().Should().Be(100);
        parts.Should().BeEquivalentTo(new long[] { 34, 33, 33 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Leftover_units_go_to_the_largest_discarded_fractions()
    {
        // 10 split by 3:3:1 is 4.28, 4.28, 1.42. Flooring gives 4, 4, 1 and leaves one unit — and it
        // goes to the *small* part, whose discarded 0.42 is larger than either 0.28. Worth pinning:
        // the intuitive answer is to give it to a big part, and that answer is wrong.
        var parts = ProportionalAllocation.Split(10, [3, 3, 1]);

        parts.Should().BeEquivalentTo(new long[] { 4, 4, 2 }, options => options.WithStrictOrdering());
        parts.Sum().Should().Be(10);
    }

    [Fact]
    public void Equal_weights_break_their_tie_by_position_every_time()
    {
        // Determinism matters because whichever worker picks the credit note up must produce the same
        // document. A sort that left equal remainders in arbitrary order would make the split depend
        // on which one ran it.
        var first = ProportionalAllocation.Split(7, [1, 1, 1, 1]);
        var second = ProportionalAllocation.Split(7, [1, 1, 1, 1]);

        first.Should().BeEquivalentTo(second, options => options.WithStrictOrdering());
        first.Should().BeEquivalentTo(new long[] { 2, 2, 2, 1 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void A_negative_total_splits_exactly_as_its_magnitude_does()
    {
        // A reversal has to mirror the charge it reverses, to the minor unit.
        var charge = ProportionalAllocation.Split(100, [7, 3]);
        var reversal = ProportionalAllocation.Split(-100, [7, 3]);

        reversal.Should().BeEquivalentTo(
            charge.Select(part => -part),
            options => options.WithStrictOrdering());
        reversal.Sum().Should().Be(-100);
    }

    [Fact]
    public void Weights_that_are_all_zero_allocate_nothing()
    {
        // With nothing to apportion by, spreading the total evenly would be an invention and giving it
        // all to the first part an arbitrary one. Neither belongs on a financial document.
        ProportionalAllocation.Split(500, [0, 0]).Should().AllSatisfy(part => part.Should().Be(0));
    }

    [Fact]
    public void A_negative_weight_is_read_as_no_entitlement()
    {
        var parts = ProportionalAllocation.Split(100, [1, -5]);

        parts.Should().BeEquivalentTo(new long[] { 100, 0 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Nothing_to_allocate_and_nothing_to_allocate_to_are_both_answered_with_zeroes()
    {
        ProportionalAllocation.Split(0, [1, 2]).Should().AllSatisfy(part => part.Should().Be(0));
        ProportionalAllocation.Split(100, []).Should().BeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(1_234_567)]
    public void Whatever_the_total_the_parts_sum_to_it(long total)
    {
        // The property, stated directly. Weights chosen to be awkward on purpose: coprime, uneven, and
        // including one that rounds to nothing at small totals.
        var parts = ProportionalAllocation.Split(total, [13, 7, 1, 29]);

        parts.Sum().Should().Be(total);
    }
}
