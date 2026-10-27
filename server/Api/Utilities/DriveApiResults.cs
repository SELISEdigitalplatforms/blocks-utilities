using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Utility.DomainService.Drive;

namespace Api.Utilities;

/// <summary>
/// Maps a drive result onto an HTTP response.
/// </summary>
/// <remarks>
/// Same shape and the same status codes for the same kinds of failure as
/// <see cref="GeolocationApiResults"/>. Two kinds deserve a note: <c>NotConnected</c> is 401
/// rather than 404 because the fix is on the auth side (reconnect), and <c>Throttled</c> is 429
/// with a <c>Retry-After</c> header so a client's generic back-off logic works without reading
/// the body.
/// </remarks>
public static class DriveApiResults
{
    /// <summary>Wraps a success in the envelope with <paramref name="successStatus"/>, or a failure with the matching status and error code.</summary>
    public static IActionResult ToActionResult<T>(this DriveResult<T> result, string correlationId, int successStatus = StatusCodes.Status200OK)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return new ObjectResult(ApiResponse<T>.Ok(result.Value!, correlationId)) { StatusCode = successStatus };
        }

        var response = new ObjectResult(ApiResponse<T>.Fail(
            ErrorCodeFor(result.FailureKind),
            result.ErrorMessage ?? "The drive request failed.",
            correlationId))
        {
            StatusCode = StatusCodeFor(result.FailureKind)
        };

        return response;
    }

    /// <summary>The HTTP status for a failure kind.</summary>
    public static int StatusCodeFor(DriveFailureKind kind) => kind switch
    {
        DriveFailureKind.InvalidRequest => StatusCodes.Status400BadRequest,
        DriveFailureKind.NotConnected => StatusCodes.Status401Unauthorized,
        DriveFailureKind.AdminConsentRequired => StatusCodes.Status403Forbidden,
        DriveFailureKind.ItemNotFound => StatusCodes.Status404NotFound,
        DriveFailureKind.NameConflict => StatusCodes.Status409Conflict,
        DriveFailureKind.FileTooLarge => StatusCodes.Status413PayloadTooLarge,
        DriveFailureKind.Throttled => StatusCodes.Status429TooManyRequests,
        DriveFailureKind.ProviderError => StatusCodes.Status502BadGateway,
        DriveFailureKind.NotConfigured => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };

    /// <summary>The machine-readable error code for a failure kind.</summary>
    public static string ErrorCodeFor(DriveFailureKind kind) => kind switch
    {
        DriveFailureKind.InvalidRequest => "drive_invalid_request",
        DriveFailureKind.NotConnected => "drive_not_connected",
        DriveFailureKind.AdminConsentRequired => "drive_admin_consent_required",
        DriveFailureKind.ItemNotFound => "drive_item_not_found",
        DriveFailureKind.NameConflict => "drive_name_conflict",
        DriveFailureKind.FileTooLarge => "drive_file_too_large",
        DriveFailureKind.Throttled => "drive_throttled",
        DriveFailureKind.ProviderError => "drive_provider_error",
        DriveFailureKind.StorageError => "drive_storage_error",
        DriveFailureKind.NotConfigured => "drive_not_configured",
        _ => "drive_failed"
    };
}
