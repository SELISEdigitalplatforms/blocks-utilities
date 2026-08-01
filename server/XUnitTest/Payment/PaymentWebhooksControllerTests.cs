using System.Reflection;
using Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace XUnitTest.Payment;

public sealed class PaymentWebhooksControllerTests
{
    [Fact]
    public void Controller_uses_a_public_route_excluded_from_api_prefix()
    {
        var controllerType = typeof(PaymentWebhooksController);

        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        var anonymous = controllerType.GetCustomAttribute<AllowAnonymousAttribute>();

        route.Should().NotBeNull();
        route!.Template.Should().Be("payments");
        anonymous.Should().NotBeNull();
        controllerType.GetCustomAttributes()
            .Should().Contain(attribute =>
                attribute.GetType().Name == "SkipGlobalApiRoutePrefixAttribute");
    }

    [Fact]
    public void Adyen_keeps_both_originally_published_endpoints()
    {
        var templates = Action(nameof(PaymentWebhooksController.Adyen))
            .GetCustomAttributes<HttpPostAttribute>()
            .Select(attribute => attribute.Template)
            .ToArray();

        templates.Should().BeEquivalentTo(
            "adyen/webhooks/standard",
            "adyen/webhooks/tokens");
    }

    [Fact]
    public void Every_provider_reaches_intake_through_one_generic_endpoint()
    {
        var httpPost = Action(nameof(PaymentWebhooksController.Provider))
            .GetCustomAttribute<HttpPostAttribute>();

        httpPost.Should().NotBeNull();
        httpPost!.Template.Should().Be("{provider}/webhooks");
    }

    [Theory]
    [InlineData(nameof(PaymentWebhooksController.Adyen))]
    [InlineData(nameof(PaymentWebhooksController.Provider))]
    public void Webhook_actions_do_not_accept_a_tenant_route_parameter(string actionName) =>
        Action(actionName).GetParameters()
            .Should().NotContain(parameter =>
                string.Equals(parameter.Name, "tenantId", StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData(nameof(PaymentWebhooksController.Adyen))]
    [InlineData(nameof(PaymentWebhooksController.Provider))]
    public void Webhook_processing_does_not_use_the_request_aborted_token(string actionName) =>
        Action(actionName).GetParameters()
            .Should().NotContain(parameter =>
                parameter.ParameterType == typeof(CancellationToken));

    [Fact]
    public void Webhook_body_is_not_deserialized_by_model_binding() =>
        Action(nameof(PaymentWebhooksController.Adyen))
            .GetParameters().Should().BeEmpty();

    private static MethodInfo Action(string actionName) =>
        typeof(PaymentWebhooksController).GetMethod(actionName)!;
}
