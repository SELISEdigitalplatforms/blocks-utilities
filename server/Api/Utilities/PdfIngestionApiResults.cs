using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Utility.DomainService.PdfIngestion;

namespace Api.Utilities;

/// <summary>
/// Maps a PDF-ingestion result onto an HTTP response.
/// </summary>
/// <remarks>
/// The same shape as <see cref="DocumentConversionApiResults"/>, and deliberately the same status
/// codes for the same kinds of failure.
/// </remarks>
public static class PdfIngestionApiResults
{
    public static IActionResult ToActionResult<TValue>(
        this PdfIngestionResult<TValue> result,
        string correlationId,
        int successStatusCode = StatusCodes.Status200OK)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return new ObjectResult(ApiResponse<TValue>.Ok(result.Value!, correlationId))
            {
                // Accepting an ingestion is a 202, not a 200: the batch has been recorded and
                // queued, not inspected yet.
                StatusCode = successStatusCode
            };
        }

        var response = ApiResponse<TValue>.Fail(
            result.ErrorCode ?? "pdf_ingestion_failed",
            result.ErrorMessage ?? "The PDF ingestion request failed.",
            correlationId,
            result.ValidationErrors?.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal));

        return new ObjectResult(response) { StatusCode = StatusCodeFor(result.FailureKind) };
    }

    private static int StatusCodeFor(PdfIngestionFailureKind kind) => kind switch
    {
        PdfIngestionFailureKind.Validation => StatusCodes.Status400BadRequest,
        PdfIngestionFailureKind.NotFound => StatusCodes.Status404NotFound,
        PdfIngestionFailureKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };
}
