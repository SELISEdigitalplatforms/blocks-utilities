using Api.Controllers;
using Api.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Payment.DomainService.Responses;
using Utility.DomainService.Drive;
using Utility.DomainService.Drive.Models;
using Utility.DomainService.Drive.Service;

namespace XUnitTest.Drive;

/// <summary>
/// Guards the status codes and envelope the drive endpoints hand back. A client that checks status codes must see a 401 for "reconnect", a 429
/// with Retry-After for throttling, and never a 200 with the failure hidden in the body.
/// </summary>
public sealed class DriveControllerTests
{
    private readonly Mock<IDriveConnectionService> _connections = new();
    private readonly Mock<IDriveService> _drive = new();
    private readonly DriveController _controller;

    public DriveControllerTests()
    {
        _controller = new DriveController(_connections.Object, _drive.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Theory]
    [InlineData(DriveFailureKind.InvalidRequest, StatusCodes.Status400BadRequest, "drive_invalid_request")]
    [InlineData(DriveFailureKind.NotConnected, StatusCodes.Status401Unauthorized, "drive_not_connected")]
    [InlineData(DriveFailureKind.AdminConsentRequired, StatusCodes.Status403Forbidden, "drive_admin_consent_required")]
    [InlineData(DriveFailureKind.ItemNotFound, StatusCodes.Status404NotFound, "drive_item_not_found")]
    [InlineData(DriveFailureKind.NameConflict, StatusCodes.Status409Conflict, "drive_name_conflict")]
    [InlineData(DriveFailureKind.FileTooLarge, StatusCodes.Status413PayloadTooLarge, "drive_file_too_large")]
    [InlineData(DriveFailureKind.Throttled, StatusCodes.Status429TooManyRequests, "drive_throttled")]
    [InlineData(DriveFailureKind.ProviderError, StatusCodes.Status502BadGateway, "drive_provider_error")]
    [InlineData(DriveFailureKind.StorageError, StatusCodes.Status500InternalServerError, "drive_storage_error")]
    [InlineData(DriveFailureKind.NotConfigured, StatusCodes.Status503ServiceUnavailable, "drive_not_configured")]
    public void Each_failure_kind_has_one_status_and_one_error_code(DriveFailureKind kind, int status, string code)
    {
        DriveApiResults.StatusCodeFor(kind).Should().Be(status);
        DriveApiResults.ErrorCodeFor(kind).Should().Be(code);
    }

    [Fact]
    public async Task A_throttled_listing_is_429_with_a_retry_after_header()
    {
        _drive.Setup(d => d.ListItemsAsync(It.IsAny<ListItemsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DriveResult<ItemsPage>.Fail(DriveFailureKind.Throttled, "slow down", 7));

        var result = await _controller.ListItems(new ListItemsRequest(), CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        _controller.Response.Headers.RetryAfter.ToString().Should().Be("7", because: "generic client back-off reads the header, not our body");
        var body = result.Value.Should().BeOfType<ApiResponse<ItemsPage>>().Subject;
        body.Success.Should().BeFalse();
        body.Error!.Code.Should().Be("drive_throttled");
    }

    [Fact]
    public async Task A_created_folder_is_201_in_the_envelope()
    {
        var item = new DriveItemSummary { ItemId = "f1", Name = "Signed", Kind = "folder" };
        _drive.Setup(d => d.CreateFolderAsync(It.IsAny<CreateFolderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DriveResult<DriveItemSummary>.Ok(item));

        var result = await _controller.CreateFolder(new CreateFolderRequest(), CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(StatusCodes.Status201Created);
        result.Value.Should().BeOfType<ApiResponse<DriveItemSummary>>().Subject.Data.Should().BeSameAs(item);
    }

    [Fact]
    public async Task Disconnect_is_204_when_something_was_removed_and_404_when_nothing_was()
    {
        _connections.SetupSequence(c => c.DisconnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DriveResult<bool>.Ok(true))
            .ReturnsAsync(DriveResult<bool>.Ok(false));

        var first = await _controller.Disconnect(CancellationToken.None);
        var second = await _controller.Disconnect(CancellationToken.None) as ObjectResult;

        first.Should().BeOfType<NoContentResult>();
        second!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }
}
