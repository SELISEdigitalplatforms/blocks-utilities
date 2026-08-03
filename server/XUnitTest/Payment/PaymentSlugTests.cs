using FluentAssertions;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentSlugTests
{
    [Fact]
    public void Unsafe_characters_are_removed_from_the_readable_part() =>
        PaymentSlug.Create("acct_1ABC").Should().StartWith("acct1ABC-");

    [Fact]
    public void Values_differing_only_by_punctuation_do_not_collide() =>
        PaymentSlug.Create("acct_1A").Should().NotBe(PaymentSlug.Create("acct-1A"));

    [Fact]
    public void The_same_value_always_produces_the_same_slug() =>
        PaymentSlug.Create("acct_1ABC").Should().Be(PaymentSlug.Create("acct_1ABC"));

    [Fact]
    public void A_value_with_no_safe_characters_still_produces_a_slug() =>
        PaymentSlug.Create("___").Should().MatchRegex("^[0-9a-f]{8}$");

    [Fact]
    public void Long_values_are_truncated_but_stay_distinct()
    {
        var first = PaymentSlug.Create(new string('a', 200) + "1");
        var second = PaymentSlug.Create(new string('a', 200) + "2");

        first.Should().NotBe(second);
        first.Length.Should().BeLessThan(60);
    }

    [Fact]
    public void Every_slug_is_safe_for_use_in_an_identifier() =>
        PaymentSlug.Create("acct_1A/B C!").Should().MatchRegex("^[A-Za-z0-9-]+$");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_missing_value_is_rejected(string? value) =>
        FluentActions.Invoking(() => PaymentSlug.Create(value!))
            .Should().Throw<ArgumentException>();
}
