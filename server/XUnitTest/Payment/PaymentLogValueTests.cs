using FluentAssertions;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// How identifiers reach the logs: system identifiers in clear so they can be searched for,
/// personal data hashed.
/// </summary>
public sealed class PaymentLogValueTests
{
    /// <summary>
    /// The whole point of the change. An operator holding a payment id from the database or the
    /// console previously had to recompute a SHA digest by hand to find its log lines, which is
    /// why the logs were unfollowable.
    /// </summary>
    [Fact]
    public void A_system_identifier_is_searchable_as_itself()
    {
        PaymentLogValue.Id("pay_01HXYZ").Should().Be("pay_01HXYZ");
    }

    /// <summary>
    /// Order and merchant identifiers come from the caller. A newline in one would end the log
    /// line and let the rest be read as entries of its own choosing.
    /// </summary>
    [Theory]
    [InlineData("order\n2026-01-01 FATAL forged entry", "order2026-01-01FATALforgedentry")]
    [InlineData("order\r\nid", "orderid")]
    [InlineData("order id", "orderid")]
    [InlineData("order\tid", "orderid")]
    public void A_caller_supplied_identifier_cannot_forge_a_log_entry(
        string hostile,
        string expected)
    {
        PaymentLogValue.Id(hostile).Should().Be(expected);
    }

    [Fact]
    public void An_identifier_is_capped_so_one_field_cannot_flood_a_line()
    {
        PaymentLogValue.Id(new string('a', 500)).Length.Should().Be(128);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_identifier_says_so_rather_than_reading_as_empty(string? value)
    {
        PaymentLogValue.Id(value).Should().Be("missing");
    }

    /// <summary>
    /// Whitespace-only reads as absent; characters that are present but all unsafe read as
    /// invalid. Two different situations, told apart so a log line says which one happened.
    /// </summary>
    [Fact]
    public void An_identifier_of_only_unsafe_characters_is_reported_as_invalid()
    {
        PaymentLogValue.Id("@@@!!!").Should().Be("invalid");
        PaymentLogValue.Id("\n\t\r").Should().Be("missing");
    }

    /// <summary>
    /// Personal data keeps its old treatment. This is the line the change deliberately did not
    /// cross: the shopper's email is not a record identifier.
    /// </summary>
    [Fact]
    public void Personal_data_is_still_hashed()
    {
        var hashed = PaymentLogValue.Hash("shopper@example.com");

        hashed.Should().NotContain("shopper");
        hashed.Should().NotContain("@");
        hashed.Length.Should().Be(16);
    }

    [Fact]
    public void The_same_value_hashes_the_same_way_so_lines_still_join()
    {
        PaymentLogValue.Hash("tenant-1")
            .Should().Be(PaymentLogValue.Hash("tenant-1"));
    }
}
