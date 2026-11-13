using System.Reflection;
using Api.Controllers;
using FluentAssertions;

namespace XUnitTest.Subscription;

/// <summary>
/// The permission the offline (bank-transfer) activation route asks for.
/// </summary>
public sealed class SubscriptionsControllerOfflinePermissionTests
{
    [Fact]
    public void Activating_offline_asks_for_its_own_permission()
    {
        ScopeOf(nameof(SubscriptionsController.ActivateOffline)).Should().Be(
            "blocks-utilities::subscription::manage-offline",
            "the only gate on a paid plan with no card is a grant the operator hands out");
    }

    [Fact]
    public void An_ordinary_subscribe_still_asks_for_the_ordinary_permission()
    {
        ScopeOf(nameof(SubscriptionsController.Subscribe)).Should().Be(
            "blocks-utilities::subscription::manage");
    }

    private static string? ScopeOf(string actionName) =>
        typeof(SubscriptionsController).GetMethod(actionName)!
            .GetCustomAttributes()
            .Where(attribute => attribute.GetType().Name.Contains(
                "ProtectedEndPoint", StringComparison.Ordinal))
            .Select(attribute => attribute.GetType()
                .GetProperties()
                .Select(property => property.GetValue(attribute) as string)
                .FirstOrDefault(value => value is not null && value.Contains("::", StringComparison.Ordinal)))
            .Single();
}
