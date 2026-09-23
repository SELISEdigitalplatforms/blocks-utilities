using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Utility.DomainService.Geolocation;

namespace Api.Utilities;

/// <summary>
/// Maps a geolocation result onto an HTTP response.
/// </summary>
/// <remarks>
/// The same shape as <see cref="DocumentConversionApiResults"/>, and deliberately the same status
/// codes for the same kinds of failure: a client that has learned how one part of this API reports
/// a validation error or a missing record should not have to learn it again here.
/// <para>
/// The payload is the lookups themselves rather than the service's response object. That object
/// carries its own <c>IsSuccess</c> and <c>ErrorMessage</c>, which the envelope already expresses;
/// nesting it would make every response state its outcome twice, and leave a client to work out
/// which of the two to believe when they disagree.
/// </para>
/// </remarks>
public static class GeolocationApiResults
{
    public static IActionResult ToActionResult(
        this LocateIpResponse result,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return new ObjectResult(
                ApiResponse<IpLookup[]>.Ok(result.IpLookups ?? [], correlationId))
            {
                StatusCode = StatusCodes.Status200OK
            };
        }

        var response = ApiResponse<IpLookup[]>.Fail(
            ErrorCodeFor(result.FailureKind),
            result.ErrorMessage ?? "The geolocation request failed.",
            correlationId);

        return new ObjectResult(response) { StatusCode = StatusCodeFor(result.FailureKind) };
    }

    private static int StatusCodeFor(GeolocationFailureKind kind) => kind switch
    {
        GeolocationFailureKind.Validation => StatusCodes.Status400BadRequest,

        // The request was well formed and no location came back. 404 describes the common case -
        // a private or unallocated address - and a provider outage lands here too, because the
        // repository cannot tell the two apart. See GeolocationFailureKind.NotFound.
        GeolocationFailureKind.NotFound => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string ErrorCodeFor(GeolocationFailureKind kind) => kind switch
    {
        GeolocationFailureKind.Validation => "geolocation_invalid_request",
        GeolocationFailureKind.NotFound => "geolocation_not_found",
        _ => "geolocation_failed"
    };
}
