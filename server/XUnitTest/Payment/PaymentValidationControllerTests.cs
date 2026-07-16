using System.Reflection;
using Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace XUnitTest.Payment;

public sealed class PaymentValidationControllerTests
{
    [Fact]
    public void Controller_uses_a_public_route_excluded_from_api_prefix()
    {
        var controllerType = typeof(PaymentValidationController);

        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        var anonymous = controllerType.GetCustomAttribute<AllowAnonymousAttribute>();

        route.Should().NotBeNull();
        route!.Template.Should().Be("payments/validate");
        anonymous.Should().NotBeNull();
        controllerType.GetCustomAttributes()
            .Should().Contain(attribute =>
                attribute.GetType().Name == "SkipGlobalApiRoutePrefixAttribute");
    }

    [Fact]
    public void ValidatePayment_uses_the_controller_level_route()
    {
        var action = typeof(PaymentValidationController)
            .GetMethod(nameof(PaymentValidationController.ValidatePayment));
        var httpGet = action!.GetCustomAttribute<HttpGetAttribute>();

        httpGet.Should().NotBeNull();
        httpGet!.Template.Should().BeNull();
    }
}
