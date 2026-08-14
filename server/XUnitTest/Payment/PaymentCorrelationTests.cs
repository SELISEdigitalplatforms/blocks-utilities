using FluentAssertions;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// The ambient correlation id that offloaded work is followed by.
/// </summary>
/// <remarks>
/// Asserted directly because every failure here is silent: a correlation that leaks between
/// items files one payment's work under another's id, and one that is lost on the way out of a
/// scope leaves the rest of a background run uncorrelated. Neither throws, and neither is
/// visible in any result — only in logs nobody can follow afterwards.
/// </remarks>
public sealed class PaymentCorrelationTests
{
    [Fact]
    public void Outside_any_flow_there_is_no_correlation()
    {
        PaymentCorrelation.IsSet.Should().BeFalse();
        PaymentCorrelation.Current.Should().Be("none");
    }

    [Fact]
    public void A_begun_correlation_is_visible_to_everything_inside_it()
    {
        using var correlation = PaymentCorrelation.Begin("trace-1");

        PaymentCorrelation.Current.Should().Be("trace-1");
        PaymentCorrelation.IsSet.Should().BeTrue();
    }

    /// <summary>
    /// The background loops process many items in one flow. An item that cleared the ambient
    /// value on the way out would leave every later item, and the loop's own lines, uncorrelated.
    /// </summary>
    [Fact]
    public void Leaving_an_item_restores_the_correlation_around_it()
    {
        using var outer = PaymentCorrelation.Begin("run-1");

        using (PaymentCorrelation.Begin("item-1"))
        {
            PaymentCorrelation.Current.Should().Be("item-1");
        }

        PaymentCorrelation.Current.Should().Be("run-1");
    }

    /// <summary>
    /// The worst outcome available: one payment's work logged under a different payment's id,
    /// which is worse than no correlation at all because it reads as evidence.
    /// </summary>
    [Fact]
    public void One_items_correlation_does_not_leak_into_the_next()
    {
        using (PaymentCorrelation.Begin("item-1"))
        {
            PaymentCorrelation.Current.Should().Be("item-1");
        }

        using (PaymentCorrelation.Begin("item-2"))
        {
            PaymentCorrelation.Current.Should().Be("item-2");
        }

        PaymentCorrelation.IsSet.Should().BeFalse();
    }

    /// <summary>
    /// Records written before the correlation fields existed carry none. Keeping the enclosing
    /// value beats overwriting it with nothing, because the enclosing run is at least a real
    /// thing to search for.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_correlation_does_not_erase_the_one_already_established(
        string? absent)
    {
        using var outer = PaymentCorrelation.Begin("run-1");
        using var inner = PaymentCorrelation.Begin(absent);

        PaymentCorrelation.Current.Should().Be("run-1");
    }

    [Fact]
    public void Disposing_twice_does_not_restore_twice()
    {
        using var outer = PaymentCorrelation.Begin("run-1");
        var inner = PaymentCorrelation.Begin("item-1");

        inner.Dispose();
        inner.Dispose();

        PaymentCorrelation.Current.Should().Be("run-1");
    }

    /// <summary>
    /// Concurrent tenants are processed on their own asynchronous flows, and AsyncLocal is what
    /// keeps them from writing over each other's identity.
    /// </summary>
    [Fact]
    public async Task Parallel_flows_keep_their_own_correlation()
    {
        var observed = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(async index =>
            {
                using var correlation = PaymentCorrelation.Begin($"flow-{index}");

                await Task.Yield();
                await Task.Delay(5);

                return PaymentCorrelation.Current;
            }));

        observed.Should().BeEquivalentTo(
            Enumerable.Range(0, 8).Select(index => $"flow-{index}"));
    }
}
