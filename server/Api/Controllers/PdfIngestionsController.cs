using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Utility.DomainService.PdfIngestion;
using Utility.DomainService.PdfIngestion.service;

namespace Api.Controllers;

/// <summary>
/// Inspecting PDFs already in storage and remediating whatever the inspection finds - unreadable
/// files, indirect page geometry, a claimed but non-compliant PDF/A profile.
/// </summary>
/// <remarks>
/// Its own controller rather than another action on <c>PdfGeneratorController</c>, for the same
/// reason <c>DocumentConversionsController</c> is: ingestion is a resource with a lifetime, not a
/// fire-and-forget queue push, and needs a status endpoint to match.
/// <para>
/// Both endpoints take a list of file IDs and answer with one outcome per file. Remediation happens
/// in place, so a file's own storage ID is the only thing supplied for it and the only thing needed
/// to poll status afterwards.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("pdf-ingestions")]
public sealed class PdfIngestionsController : ControllerBase
{
    private readonly IPdfIngestionService _ingestions;

    public PdfIngestionsController(IPdfIngestionService ingestions) =>
        _ingestions = ingestions;

    /// <summary>
    /// Queues one or more PDFs for ingestion.
    /// </summary>
    /// <remarks>
    /// Returns 202 as soon as the batch is recorded and queued - no file in it has been inspected
    /// yet. Each file in <c>fileIds</c> is accepted or rejected independently: one blank or duplicate
    /// ID does not stop the rest of the batch, and the response's <c>results</c> array says which is
    /// which.
    /// <para>
    /// Supplying <c>messageCoRelationId</c> also gets a completion notification per file as each one
    /// finishes. Setting <c>dryRun</c> records and reports each file's verdict without uploading any
    /// remediated bytes back to storage - useful for the first production rollout of an otherwise
    /// destructive in-place feature.
    /// </para>
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(
        typeof(ApiResponse<IngestPdfsBatchResponse>),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(
        typeof(ApiResponse<IngestPdfsBatchResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<IngestPdfsBatchResponse>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ingest(
        [FromBody] IngestPdfsRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _ingestions.RequestIngestionsAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId, StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// Reads the ingestion outcome of one or more files, including each one's full verdict once its
    /// pipeline run has completed.
    /// </summary>
    /// <remarks>
    /// The fallback for a completion notification that never arrived. A file still running answers
    /// with <c>status</c> of <c>Queued</c> or <c>Processing</c> and <c>isComplete: false</c>. A file
    /// never submitted for ingestion answers with <c>found: false</c> rather than dropping out of the
    /// response, so the caller can match every ID they asked about against exactly one result.
    /// <para>
    /// The request as a whole is 400 only when it is structurally invalid - an empty or oversized
    /// <c>fileIds</c> list. A per-file outcome is carried in that file's own result entry, not the
    /// HTTP status, because one response can only carry one status code for the whole batch.
    /// </para>
    /// </remarks>
    [HttpPost("status")]
    [ProducesResponseType(
        typeof(ApiResponse<PdfIngestionStatusBatchResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<PdfIngestionStatusBatchResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetStatus(
        [FromBody] GetPdfIngestionStatusRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _ingestions.GetStatusAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
