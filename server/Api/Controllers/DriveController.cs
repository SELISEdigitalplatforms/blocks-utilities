using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Utility.DomainService.Drive;
using Utility.DomainService.Drive.Models;
using Utility.DomainService.Drive.Service;

namespace Api.Controllers;

/// <summary>
/// OneDrive and SharePoint for the calling user: connect a Microsoft account, browse it, create
/// folders, import files into Blocks Storage, and push Blocks Storage files into it (spec 004).
/// </summary>
/// <remarks>
/// API only; nothing in the console shows it. The consumer is the SELISE Signature app, which
/// lets a user pick a PDF from their drive and later save the signed archive back. Every
/// endpoint acts on the connection of the user in the bearer token inside the project in
/// <c>x-blocks-key</c>; no request can name another user. The Entra callback lives in
/// <see cref="DriveCallbackController"/>, outside the <c>api</c> prefix.
/// </remarks>
[ApiController]
[Route("drive")]
public sealed class DriveController : ControllerBase
{
    private readonly IDriveConnectionService _connections;
    private readonly IDriveService _drive;

    /// <summary>Initializes a new instance of the <see cref="DriveController"/> class.</summary>
    public DriveController(IDriveConnectionService connections, IDriveService drive)
    {
        _connections = connections;
        _drive = drive;
    }

    /// <summary>
    /// Starts connecting the caller's Microsoft account. Returns the Entra authorize URL to send
    /// the browser to, or <c>connected: true</c> when a connection already exists.
    /// </summary>
    /// <remarks>
    /// <c>returnUrl</c> is where the browser lands after Entra, with <c>?driveStatus=connected</c>,
    /// <c>failed&amp;reason=...</c> or <c>admin_consent_required</c> appended. It must be on one of
    /// the project's registered domains; anything else is refused so the callback cannot become an
    /// open redirect. The state behind the URL is single use and expires after ten minutes.
    /// </remarks>
    [HttpPost("connections")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<StartConnectionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StartConnectionResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<StartConnectionResponse>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StartConnection([FromBody] StartConnectionRequest request, CancellationToken cancellationToken)
    {
        var result = await _connections.StartAsync(request, cancellationToken);
        return result.ToActionResult(HttpContext.TraceIdentifier);
    }

    /// <summary>Whether the caller has a connection, and the admin-consent URL to show an IT admin if needed.</summary>
    [HttpGet("connections/me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ConnectionStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetConnection(CancellationToken cancellationToken)
    {
        var result = await _connections.GetStatusAsync(cancellationToken);
        return result.ToActionResult(HttpContext.TraceIdentifier);
    }

    /// <summary>Forgets the caller's connection. The next operation will say <c>drive_not_connected</c>.</summary>
    [HttpDelete("connections/me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        var result = await _connections.DisconnectAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ToActionResult(HttpContext.TraceIdentifier);
        }

        return result.Value
            ? NoContent()
            : new ObjectResult(ApiResponse<bool>.Fail("drive_not_connected", "No Microsoft account is connected for this user.", HttpContext.TraceIdentifier))
            {
                StatusCode = StatusCodes.Status404NotFound
            };
    }

    /// <summary>
    /// SharePoint sites the caller can browse. With <c>search</c>, sites matching the text;
    /// without, the sites the user follows.
    /// </summary>
    [HttpGet("sites")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SiteSummary>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SiteSummary>>), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SiteSummary>>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ListSites([FromQuery] ListSitesRequest request, CancellationToken cancellationToken)
    {
        var result = await _drive.ListSitesAsync(request, cancellationToken);
        return WithRetryAfter(result.ToActionResult(HttpContext.TraceIdentifier), result.RetryAfterSeconds);
    }

    /// <summary>
    /// Children of a folder (or the drive root), or a search within it. Forward-only paging through
    /// <c>cursor</c>; Graph cannot sort search results, and <c>sortApplied</c> says when it did not.
    /// </summary>
    [HttpGet("items")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ItemsPage>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ItemsPage>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ItemsPage>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ItemsPage>), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiResponse<ItemsPage>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ListItems([FromQuery] ListItemsRequest request, CancellationToken cancellationToken)
    {
        var result = await _drive.ListItemsAsync(request, cancellationToken);
        return WithRetryAfter(result.ToActionResult(HttpContext.TraceIdentifier), result.RetryAfterSeconds);
    }

    /// <summary>Creates a folder. A name that already exists is a 409, never a silent rename.</summary>
    [HttpPost("folders")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<DriveItemSummary>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<DriveItemSummary>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<DriveItemSummary>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<DriveItemSummary>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderRequest request, CancellationToken cancellationToken)
    {
        var result = await _drive.CreateFolderAsync(request, cancellationToken);
        return WithRetryAfter(result.ToActionResult(HttpContext.TraceIdentifier, StatusCodes.Status201Created), result.RetryAfterSeconds);
    }

    /// <summary>
    /// Copies a drive file into Blocks Storage under the caller's project and returns the new
    /// file id. Streamed; refused up front when the drive reports a size over the limit.
    /// </summary>
    [HttpPost("imports")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ImportFileResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<ImportFileResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ImportFileResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ImportFileResponse>), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiResponse<ImportFileResponse>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Import([FromBody] ImportFileRequest request, CancellationToken cancellationToken)
    {
        var result = await _drive.ImportAsync(request, cancellationToken);
        return WithRetryAfter(result.ToActionResult(HttpContext.TraceIdentifier, StatusCodes.Status201Created), result.RetryAfterSeconds);
    }

    /// <summary>
    /// Pushes Blocks Storage files into a drive folder. One result per file; a failure in one does
    /// not undo the others, and a name clash is resolved by Graph renaming the new file.
    /// </summary>
    [HttpPost("uploads")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<UploadFilesResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UploadFilesResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UploadFilesResponse>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Upload([FromBody] UploadFilesRequest request, CancellationToken cancellationToken)
    {
        var result = await _drive.UploadAsync(request, cancellationToken);
        return WithRetryAfter(result.ToActionResult(HttpContext.TraceIdentifier), result.RetryAfterSeconds);
    }

    private IActionResult WithRetryAfter(IActionResult result, int? retryAfterSeconds)
    {
        if (retryAfterSeconds is { } seconds)
        {
            Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return result;
    }
}
