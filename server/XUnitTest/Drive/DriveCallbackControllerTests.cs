using Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Utility.DomainService.Drive.Models;
using Utility.DomainService.Drive.Service;

namespace XUnitTest.Drive;

/// <summary>
/// Guards the Entra callback's redirect-or-400 rule and its route. The route test exists because
/// the callback once lived under <c>api/</c>, where Genesis's tenant validation answered Entra's
/// redirect with 406 Invalid_Origin_Or_Referer and nobody could ever connect a drive.
/// </summary>
public sealed class DriveCallbackControllerTests
{
    private readonly Mock<IDriveConnectionService> _connections = new();
    private readonly DriveCallbackController _controller;

    public DriveCallbackControllerTests()
    {
        _controller = new DriveCallbackController(_connections.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public void The_callback_is_anonymous_and_outside_the_api_prefix()
    {
        var attributes = typeof(DriveCallbackController).GetCustomAttributes(inherit: false);

        attributes.Should().ContainSingle(a => a is AllowAnonymousAttribute, because: "Entra has no Blocks token");
        attributes.Should().ContainSingle(a => a.GetType().Name == "SkipGlobalApiRoutePrefixAttribute",
            because: "under api/ the Genesis origin check rejects the login.microsoftonline.com referer with 406");
        attributes.OfType<RouteAttribute>().Single().Template.Should().Be("drive/connections/callback",
            because: "the Entra app registration and Drive:RedirectUrl point here");
    }

    [Fact]
    public async Task The_callback_redirects_when_the_service_has_a_return_url_and_400s_otherwise()
    {
        _connections.Setup(c => c.CompleteAsync("code", "good", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectionCallbackOutcome { RedirectUrl = "https://sign.example.com/done?driveStatus=connected", Status = "connected" });
        _connections.Setup(c => c.CompleteAsync(It.IsAny<string?>(), "bad", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectionCallbackOutcome { Status = "failed" });

        var good = await _controller.Callback("code", "good", null, null, null, CancellationToken.None);
        var bad = await _controller.Callback("code", "bad", null, null, null, CancellationToken.None);

        good.Should().BeOfType<RedirectResult>().Subject.Url.Should().Be("https://sign.example.com/done?driveStatus=connected");
        bad.Should().BeOfType<BadRequestObjectResult>(because: "there is no trusted place to send an unknown state");
    }

    [Fact]
    public async Task An_admin_consent_round_trip_without_state_gets_a_plain_page_and_touches_no_state()
    {
        var result = await _controller.Callback(null, null, null, null, "True", CancellationToken.None);

        result.Should().BeOfType<ContentResult>().Subject.Content.Should().Contain("Admin consent recorded");
        _connections.Verify(c => c.CompleteAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
