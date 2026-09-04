using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.service;

namespace Api.Controllers;

/// <summary>
/// Converting a word-processing document (.doc, .docx, .rtf, .odt, ...) into a PDF.
/// </summary>
/// <remarks>
/// Its own controller rather than another action on <c>PdfGeneratorController</c>, because a
/// conversion is a resource with a lifetime: it is created, it progresses, and it can be asked
/// about afterwards. The PDF generator's other operations are fire-and-forget queue pushes that
/// keep no record and have nothing to expose.
/// <para>
/// The PDF replaces the source file, so the document's own storage ID is the only thing a caller
/// supplies — its name, extension and directory all come from the file's storage record.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("document-conversions")]
public sealed class DocumentConversionsController : ControllerBase
{
    private readonly IDocumentConversionService _conversions;

    public DocumentConversionsController(IDocumentConversionService conversions) =>
        _conversions = conversions;

    /// <summary>
    /// Queues a document for conversion to PDF.
    /// </summary>
    /// <remarks>
    /// Returns 202 as soon as the conversion is recorded and queued — the document is not converted
    /// yet. The response carries a <c>conversionId</c> and the <c>statusUrl</c> to poll.
    /// <para>
    /// Supplying <c>messageCoRelationId</c> also gets a completion notification. It is optional, and
    /// omitting it changes nothing about the conversion itself.
    /// </para>
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(
        typeof(ApiResponse<ConvertDocumentToPdfAcceptedResponse>),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(
        typeof(ApiResponse<ConvertDocumentToPdfAcceptedResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<ConvertDocumentToPdfAcceptedResponse>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Convert(
        [FromBody] ConvertDocumentToPdfRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _conversions.RequestConversionAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId, StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// Reads a conversion's outcome, and where to download the PDF once there is one.
    /// </summary>
    /// <remarks>
    /// The fallback for a completion notification that never arrived. A conversion that is still
    /// running answers 200 with <c>status</c> of <c>Queued</c> or <c>Processing</c> and
    /// <c>isComplete: false</c> — a poller stops when <c>isComplete</c> turns true, whether the
    /// conversion succeeded or failed.
    /// <para>
    /// A failed conversion is still a 200: the question "what happened to this conversion?" was
    /// answered. <c>errorCode</c> carries why it failed. 404 means no such conversion, which is a
    /// different sentence entirely.
    /// </para>
    /// <para>
    /// <c>downloadUrl</c> is resolved on each request because storage URLs expire, and is null until
    /// the conversion succeeds.
    /// </para>
    /// </remarks>
    [HttpGet("{conversionId}")]
    [ProducesResponseType(
        typeof(ApiResponse<DocumentConversionStatusResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<DocumentConversionStatusResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<DocumentConversionStatusResponse>),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(
        [FromRoute] string conversionId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _conversions.GetStatusAsync(conversionId, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
