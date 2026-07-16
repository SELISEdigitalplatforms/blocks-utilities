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
        route!.Template.Should().Be("payments/adyen/webhooks");
        anonymous.Should().NotBeNull();
        controllerType.GetCustomAttributes()
            .Should().Contain(attribute =>
                attribute.GetType().Name == "SkipGlobalApiRoutePrefixAttribute");
    }

    [Theory]
    [InlineData(
        nameof(PaymentWebhooksController.Standard),
        "standard")]
    [InlineData(
        nameof(PaymentWebhooksController.Tokens),
        "tokens")]
    public void Webhook_actions_use_the_controller_level_public_route(
        string actionName,
        string actionRoute)
    {
        var action = typeof(PaymentWebhooksController)
            .GetMethod(actionName);

        var httpPost = action!.GetCustomAttribute<HttpPostAttribute>();

        httpPost.Should().NotBeNull();
        httpPost!.Template.Should().Be(actionRoute);
    }

    [Theory]
    [InlineData(nameof(PaymentWebhooksController.Standard))]
    [InlineData(nameof(PaymentWebhooksController.Tokens))]
    public void Webhook_actions_do_not_accept_a_tenant_route_parameter(
        string actionName)
    {
        var action = typeof(PaymentWebhooksController)
            .GetMethod(actionName);

        action.Should().NotBeNull();
        action!.GetParameters()
            .Should().NotContain(parameter =>
                string.Equals(
                    parameter.Name,
                    "tenantId",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Standard_webhook_processing_does_not_use_the_request_aborted_token()
    {
        var action = typeof(PaymentWebhooksController)
            .GetMethod(nameof(PaymentWebhooksController.Standard));

        action.Should().NotBeNull();
        action!.GetParameters()
            .Should().NotContain(parameter =>
                parameter.ParameterType == typeof(CancellationToken));
    }
}
