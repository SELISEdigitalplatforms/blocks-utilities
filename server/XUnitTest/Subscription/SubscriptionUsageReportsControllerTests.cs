using System.Reflection;
using Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace XUnitTest.Subscription;

/// <summary>
/// The tenant-usage-analytics endpoints' authorization gate.
/// </summary>
/// <remarks>
/// Unlike every other subscription read, which is scoped to the caller's own organization by
/// construction, this controller crosses organizations within a tenant — so it is gated by its own
/// claim (<c>subscription.usage-report.read</c>, registered in <c>Program.cs</c> under the
/// <c>SubscriptionUsageReportReader</c> policy) rather than the general authenticated-user bar.
/// Asserting the exact policy name, not merely that some <see cref="AuthorizeAttribute"/> is
/// present, is what catches a typo or an accidental swap to a weaker policy.
/// </remarks>
public sealed class SubscriptionUsageReportsControllerTests
{
    [Fact]
    public void The_controller_requires_the_usage_report_reader_policy()
    {
        typeof(SubscriptionUsageReportsController)
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy
            .Should().Be(
                "SubscriptionUsageReportReader",
                "tenant-wide usage reporting crosses organizations and must never be reachable " +
                "under a weaker or different policy, or under mere authentication alone");
    }
}
