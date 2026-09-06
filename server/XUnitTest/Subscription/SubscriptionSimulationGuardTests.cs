using FluentAssertions;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Simulation;

namespace XUnitTest.Subscription;

public sealed class SubscriptionSimulationGuardTests
{
    private static readonly PaymentOptions Options = new() { ConsoleOrganizationId = "console-org" };

    [Fact]
    public void Refuses_a_non_console_caller_even_with_the_permission()
    {
        SubscriptionSimulationGuard.IsAuthorized(
                "some-other-org", Options, [SubscriptionSimulationGuard.SimulationAdministratorPermission])
            .Should().BeFalse();
    }

    [Fact]
    public void Refuses_the_console_without_the_permission()
    {
        SubscriptionSimulationGuard.IsAuthorized(
                "console-org", Options, ["some-other-permission"])
            .Should().BeFalse();
    }

    [Fact]
    public void Refuses_the_console_with_no_permissions_at_all()
    {
        SubscriptionSimulationGuard.IsAuthorized("console-org", Options, null)
            .Should().BeFalse();
    }

    [Fact]
    public void Refuses_a_caller_with_no_organization()
    {
        SubscriptionSimulationGuard.IsAuthorized(
                null, Options, [SubscriptionSimulationGuard.SimulationAdministratorPermission])
            .Should().BeFalse(
                "a caller with no organization is a tenant-wide integration, not the console, " +
                "the same rule PaymentOrganizationScope already applies");
    }

    [Fact]
    public void Allows_the_console_with_the_permission()
    {
        SubscriptionSimulationGuard.IsAuthorized(
                "console-org", Options, [SubscriptionSimulationGuard.SimulationAdministratorPermission])
            .Should().BeTrue();
    }

    [Fact]
    public void Refuses_everyone_when_the_console_override_is_turned_off()
    {
        var noConsole = new PaymentOptions { ConsoleOrganizationId = "" };

        SubscriptionSimulationGuard.IsAuthorized(
                "console-org", noConsole, [SubscriptionSimulationGuard.SimulationAdministratorPermission])
            .Should().BeFalse();
    }
}
