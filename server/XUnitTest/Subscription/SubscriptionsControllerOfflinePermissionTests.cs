using System.Reflection;
using Api.Controllers;
using FluentAssertions;

namespace XUnitTest.Subscription;

/// <summary>
/// The permission the offline (bank-transfer) routes ask for. Spec 006.
/// </summary>
/// <remarks>
/// Offline routes once asked for a permission of their own, <c>subscription-offline::manage</c>,
/// that no role carried, so a console admin who could manage every other subscription was refused
/// with a 403 here. They take the ordinary <c>subscription::manage</c>; keeping organizations from
/// activating or re-pricing themselves is the service's console-only check, not this attribute.
/// </remarks>
public sealed class SubscriptionsControllerOfflinePermissionTests
{
    [Theory]
    [InlineData(nameof(SubscriptionsController.ActivateOffline))]
    [InlineData(nameof(SubscriptionsController.ChangePlanOffline))]
    [InlineData(nameof(SubscriptionsController.RepriceOffline))]
    public void An_offline_action_asks_for_the_same_permission_as_an_ordinary_subscription(string actionName)
    {
        var action = typeof(SubscriptionsController).GetMethod(actionName)!;

        var scopes = action
            .GetCustomAttributes()
            .Where(attribute => attribute.GetType().Name.Contains(
                "ProtectedEndPoint", StringComparison.Ordinal))
            .Select(attribute => attribute.GetType()
                .GetProperties()
                .Select(property => property.GetValue(attribute) as string)
                .FirstOrDefault(value => value is not null && value.Contains("::", StringComparison.Ordinal)))
            .ToList();

        scopes.Should().ContainSingle()
            .Which.Should().Be(
                "blocks-utilities::subscription::manage",
                "a console admin who can manage subscriptions must not need a separate grant to " +
                "bill a customer by invoice");
    }
}
