using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Utility.DomainService.PdfGenerator.service;

namespace Api.Utilities;

/// <summary>
/// Maps a document-conversion result onto an HTTP response.
/// </summary>
/// <remarks>
/// The same shape as <see cref="SubscriptionApiResults"/>, and deliberately the same status codes
/// for the same kinds of failure: a client that has learned how one part of this API reports a
/// validation error or a missing record should not have to learn it again here.
/// </remarks>
public static class DocumentConversionApiResults
{
    public static IActionResult ToActionResult<TValue>(
        this DocumentConversionResult<TValue> result,
        string correlationId,
        int successStatusCode = StatusCodes.Status200OK)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return new ObjectResult(ApiResponse<TValue>.Ok(result.Value!, correlationId))
            {
                // Accepting a conversion is a 202, not a 200: the work has been recorded and queued,
                // not done. A 200 would tell a client the document is converted when it is not.
                StatusCode = successStatusCode
            };
        }

        var response = ApiResponse<TValue>.Fail(
            result.ErrorCode ?? "document_conversion_failed",
            result.ErrorMessage ?? "The document conversion request failed.",
            correlationId,
            result.ValidationErrors?.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal));

        return new ObjectResult(response) { StatusCode = StatusCodeFor(result.FailureKind) };
    }

    private static int StatusCodeFor(DocumentConversionFailureKind kind) => kind switch
    {
        DocumentConversionFailureKind.Validation => StatusCodes.Status400BadRequest,
        DocumentConversionFailureKind.NotFound => StatusCodes.Status404NotFound,
        // The request was well formed and the file exists; it is the document itself this service
        // cannot process, which is what 422 says and 400 does not.
        DocumentConversionFailureKind.Unsupported => StatusCodes.Status422UnprocessableEntity,
        DocumentConversionFailureKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };
}
