using System.Reflection;
using System.Text.RegularExpressions;
using Api.Controllers;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace XUnitTest.Api;

/// <summary>
/// Every HTTP action in the API names the permission that grants it, or is deliberately anonymous.
/// </summary>
/// <remarks>
/// Guards against an endpoint that only checks authentication: any signed-in user of the tenant,
/// whatever their role, could then send SMS, read tenant data through the template engine or reset
/// the sequences invoice numbers come from.
/// </remarks>
public sealed partial class EndpointAuthorizationTests
{
    /// <summary>
    /// Called by providers and by link recipients, not by tenant users; each authenticates by
    /// signature or by the link itself. Adding to this list is a security decision.
    /// </summary>
    private static readonly string[] AnonymousActions =
    [
        "MagicLinkController.Invoke",
        "PaymentValidationController.*",
        "PaymentWebhooksController.*",
        "SmsWebhooksController.*"
    ];

    [Fact]
    public void Every_action_is_protected_or_deliberately_anonymous()
    {
        var unprotected = Actions()
            .Where(action => !IsAnonymous(action) && Resource(action) == null)
            .Select(Name)
            .ToList();

        string.Join(", ", unprotected).Should().BeEmpty(
            "an action without [ProtectedEndPoint] is open to every authenticated user of the tenant");
    }

    [Fact]
    public void Only_the_known_actions_are_anonymous()
    {
        var anonymous = Actions()
            .Where(IsAnonymous)
            .Select(action => action.DeclaringType!.GetCustomAttribute<AllowAnonymousAttribute>() != null
                ? $"{action.DeclaringType!.Name}.*"
                : Name(action))
            .Distinct()
            .ToList();

        anonymous.Should().BeEquivalentTo(AnonymousActions,
            "a new anonymous endpoint is reachable without any token and must be added here on purpose");
    }

    [Fact]
    public void Every_resource_follows_the_service_controller_action_naming()
    {
        var malformed = Actions()
            .Select(Resource)
            .Where(resource => resource != null && !ResourcePattern().IsMatch(resource))
            .ToList();

        string.Join(", ", malformed).Should().BeEmpty(
            "Blocks OS grants a resource by its exact name, so a typo answers 403 for everyone");
    }

    private static IEnumerable<MethodInfo> Actions() =>
        typeof(PaymentsController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any());

    private static bool IsAnonymous(MethodInfo action) =>
        action.GetCustomAttribute<AllowAnonymousAttribute>() != null ||
        action.DeclaringType!.GetCustomAttribute<AllowAnonymousAttribute>() != null;

    private static string? Resource(MethodInfo action) =>
        action.GetCustomAttributesData()
            .Concat(action.DeclaringType!.GetCustomAttributesData())
            .FirstOrDefault(data => data.AttributeType == typeof(ProtectedEndPointAttribute))
            ?.ConstructorArguments.FirstOrDefault().Value as string;

    private static string Name(MethodInfo action) => $"{action.DeclaringType!.Name}.{action.Name}";

    [GeneratedRegex(@"^blocks-utilities::[a-z]+(-[a-z]+)*::([a-z]+(-[a-z]+)*|\*)$")]
    private static partial Regex ResourcePattern();
}
