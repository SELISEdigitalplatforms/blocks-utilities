using FluentAssertions;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Simulation;

namespace XUnitTest.Subscription;

/// <summary>
/// The guard decides console scope and nothing else. Whether the caller carries
/// <c>subscription.simulation.*</c> is the framework's answer, given before any of this runs.
/// </summary>
public sealed class SubscriptionSimulationGuardTests
{
    private static readonly PaymentOptions Options = new() { ConsoleOrganizationId = "console-org" };

    [Fact]
    public void Refuses_a_caller_who_is_not_the_console()
    {
        SubscriptionSimulationGuard.IsAuthorized("some-other-org", Options)
            .Should().BeFalse();
    }

    [Fact]
    public void Refuses_a_caller_with_no_organization()
    {
        SubscriptionSimulationGuard.IsAuthorized(null, Options)
            .Should().BeFalse(
                "a caller with no organization is a tenant-wide integration, not the console, " +
                "the same rule PaymentOrganizationScope already applies");
    }

    [Fact]
    public void Allows_the_console()
    {
        SubscriptionSimulationGuard.IsAuthorized("console-org", Options)
            .Should().BeTrue();
    }

    [Fact]
    public void Refuses_everyone_when_the_console_override_is_turned_off()
    {
        var noConsole = new PaymentOptions { ConsoleOrganizationId = "" };

        SubscriptionSimulationGuard.IsAuthorized("console-org", noConsole)
            .Should().BeFalse();
    }
}
