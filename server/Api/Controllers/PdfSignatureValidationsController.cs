using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Utility.DomainService.PdfSignatureValidation;
using Utility.DomainService.PdfSignatureValidation.service;

namespace Api.Controllers;

/// <summary>
/// Validating the signatures of PDFs already in storage with EU DSS: whether each signature holds,
/// which PAdES level it reaches, and whether the revocation data embedded in the file is what
/// validation relied on.
/// </summary>
/// <remarks>
/// Its own controller rather than more actions on <c>PdfIngestionsController</c>: validation never
/// changes the file and has its own verdict, but it follows ingestion's contract - a batch of file
/// IDs in, one outcome per file, a POST status endpoint to poll - and ingestion's status-code
/// mapping. See docs/pdf-signature-validation/README.md.
/// </remarks>
[ApiController]
[Authorize]
[Route("pdf-signature-validations")]
public sealed class PdfSignatureValidationsController : ControllerBase
{
    private readonly IPdfSignatureValidationService _validations;

    public PdfSignatureValidationsController(IPdfSignatureValidationService validations) =>
        _validations = validations;

    /// <summary>
    /// Queues the signatures of one or more PDFs for validation.
    /// </summary>
    /// <remarks>
    /// Returns 202 as soon as the batch is recorded and queued; no file has been validated yet. The
    /// response has one result per entry sent, in order. A blank ID, or a repeat of an ID earlier in
    /// the list, is rejected on its own without stopping the rest. The request as a whole is 400
    /// only when <c>fileIds</c> is empty or has more than 50 entries.
    /// <para>
    /// Requesting a file that is already queued or running is accepted: the earlier run is
    /// superseded and only this request's verdict is kept. Supplying <c>messageCoRelationId</c>
    /// also gets a completion notification per file.
    /// </para>
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(
        typeof(ApiResponse<ValidatePdfSignaturesBatchResponse>),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(
        typeof(ApiResponse<ValidatePdfSignaturesBatchResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<ValidatePdfSignaturesBatchResponse>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Validate(
        [FromBody] ValidatePdfSignaturesRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _validations.RequestValidationsAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId, StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// Reads the validation outcome of one or more files, including each verdict once complete.
    /// </summary>
    /// <remarks>
    /// A file still running answers with <c>status</c> <c>Queued</c> or <c>Processing</c> and
    /// <c>isComplete: false</c>. A file never submitted, or submitted by another tenant, answers
    /// <c>found: false</c>. Blank IDs are ignored and a repeated ID is answered once, so there is one
    /// result per distinct ID; the request is 400 only when no usable ID remains or more than 50 do.
    /// </remarks>
    [HttpPost("status")]
    [ProducesResponseType(
        typeof(ApiResponse<PdfSignatureValidationStatusBatchResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<PdfSignatureValidationStatusBatchResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetStatus(
        [FromBody] GetPdfSignatureValidationStatusRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _validations.GetStatusAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
