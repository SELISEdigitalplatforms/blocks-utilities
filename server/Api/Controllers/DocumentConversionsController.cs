using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.service;

namespace Api.Controllers;

/// <summary>
/// Converting word-processing documents (.doc, .docx, .rtf, .odt, ...) into PDF.
/// </summary>
/// <remarks>
/// Its own controller rather than another action on <c>PdfGeneratorController</c>, because a
/// conversion is a resource with a lifetime: it is created, it progresses, and it can be asked
/// about afterwards. The PDF generator's other operations are fire-and-forget queue pushes that
/// keep no record and have nothing to expose.
/// <para>
/// Both endpoints take a list of file IDs and answer with one outcome per file, because a caller
/// converting or checking on several documents should not have to make one call per document. Each
/// PDF replaces its own source file, so a file's own storage ID is the only thing supplied for it —
/// its name, extension and directory all come from the file's storage record.
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
    /// Queues one or more documents for conversion to PDF.
    /// </summary>
    /// <remarks>
    /// Returns 202 as soon as the batch is recorded and queued — no document in it is converted yet.
    /// Each file in <c>fileIds</c> is accepted or rejected independently: one blank or duplicate ID
    /// does not stop the rest of the batch, and the response's <c>results</c> array says which is
    /// which. Polling uses a file's own ID, the one just sent in, so no second identifier is issued
    /// for the caller to keep track of.
    /// <para>
    /// Supplying <c>messageCoRelationId</c> also gets a completion notification per file as each one
    /// finishes. It is optional, and omitting it changes nothing about the conversions themselves.
    /// </para>
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(
        typeof(ApiResponse<ConvertDocumentsToPdfBatchResponse>),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(
        typeof(ApiResponse<ConvertDocumentsToPdfBatchResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<ConvertDocumentsToPdfBatchResponse>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Convert(
        [FromBody] ConvertDocumentToPdfRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _conversions.RequestConversionsAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId, StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// Reads the conversion outcome of one or more files, and where to download each PDF once there
    /// is one.
    /// </summary>
    /// <remarks>
    /// The fallback for a completion notification that never arrived. A POST rather than a GET, even
    /// though this only reads: the query needs to carry a list of file IDs, and a body on a GET is
    /// inconsistently supported by clients, proxies and framework model binding.
    /// <para>
    /// A file that is still running answers with <c>status</c> of <c>Queued</c> or <c>Processing</c>
    /// and <c>isComplete: false</c> — a poller stops asking about that file once <c>isComplete</c>
    /// turns true, whether the conversion succeeded or failed. A file that was never submitted for
    /// conversion answers with <c>found: false</c> rather than dropping out of the response, so the
    /// caller can match every ID they asked about against exactly one result.
    /// </para>
    /// <para>
    /// The request as a whole is 400 only when it is structurally invalid — an empty or oversized
    /// <c>fileIds</c> list. A per-file outcome such as "never submitted" or "failed" is carried in
    /// that file's own result entry, not the HTTP status, because one response can only carry one
    /// status code for the whole batch.
    /// </para>
    /// <para>
    /// <c>downloadUrl</c> is resolved on each request because storage URLs expire, and is null until
    /// a given file's conversion succeeds.
    /// </para>
    /// </remarks>
    [HttpPost("status")]
    [ProducesResponseType(
        typeof(ApiResponse<DocumentConversionStatusBatchResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<DocumentConversionStatusBatchResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetStatus(
        [FromBody] GetDocumentConversionStatusRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _conversions.GetStatusAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
