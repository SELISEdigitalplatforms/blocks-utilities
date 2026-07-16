using FluentAssertions;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentRateLimitResultTests
{
    [Fact]
    public void Restrictiveness_compares_remaining_capacity_ratio_not_raw_token_count()
    {
        var tenant = Result(limit: 300, remaining: 20);
        var order = Result(limit: 10, remaining: 9);

        tenant.IsMoreRestrictiveThan(order).Should().BeTrue();
        order.IsMoreRestrictiveThan(tenant).Should().BeFalse();
    }

    [Fact]
    public void Equal_ratio_prefers_the_bucket_with_fewer_absolute_tokens()
    {
        var order = Result(limit: 10, remaining: 1);
        var tenant = Result(limit: 100, remaining: 10);

        order.IsMoreRestrictiveThan(tenant).Should().BeTrue();
        tenant.IsMoreRestrictiveThan(order).Should().BeFalse();
    }

    [Fact]
    public void Complete_tie_prefers_the_bucket_with_the_longer_reset_time()
    {
        var slowerRecovery = Result(limit: 10, remaining: 1, resetAfterSeconds: 30);
        var fasterRecovery = Result(limit: 10, remaining: 1, resetAfterSeconds: 6);

        slowerRecovery.IsMoreRestrictiveThan(fasterRecovery).Should().BeTrue();
        fasterRecovery.IsMoreRestrictiveThan(slowerRecovery).Should().BeFalse();
    }

    [Fact]
    public void Identical_buckets_do_not_replace_each_other()
    {
        var first = Result(limit: 10, remaining: 5, resetAfterSeconds: 10);
        var second = Result(limit: 10, remaining: 5, resetAfterSeconds: 10);

        first.IsMoreRestrictiveThan(second).Should().BeFalse();
        second.IsMoreRestrictiveThan(first).Should().BeFalse();
    }

    private static PaymentRateLimitResult Result(
        int limit,
        int remaining,
        int resetAfterSeconds = 1) => new()
    {
        IsAllowed = true,
        Limit = limit,
        Remaining = remaining,
        ResetAfterSeconds = resetAfterSeconds
    };
}
