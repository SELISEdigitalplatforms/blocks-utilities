using System.Globalization;
using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Api.Controllers;

/// <summary>
/// Metered usage. Served under <c>/api/subscription-usage</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("subscription-usage")]
public sealed class SubscriptionUsageController : ControllerBase
{
    private readonly IUsageRecordingService _usage;
    private readonly ISubscriptionUsageOveragePreviewService _overagePreview;

    public SubscriptionUsageController(
        IUsageRecordingService usage,
        ISubscriptionUsageOveragePreviewService overagePreview)
    {
        _usage = usage;
        _overagePreview = overagePreview;
    }

    /// <summary>
    /// Records usage and reports where that leaves the allowance.
    /// </summary>
    /// <remarks>
    /// This is the authoritative gate, not <c>GET /api/entitlements</c>. The figures returned
    /// include this call, so two callers at the boundary get different answers; a caller that
    /// must not exceed its allowance should set <c>enforce</c> and act on <c>allowed</c>.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<UsageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UsageResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UsageResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Record(
        [FromBody] RecordUsageRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _usage.RecordAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Current usage for every meter on the organization's live subscription.
    /// </summary>
    /// <remarks>
    /// <c>readMode</c> chooses where the figures come from and nothing else. It is a performance
    /// choice, never an authorisation one: neither mode may be used to decide whether usage is
    /// allowed. Only <c>POST /api/subscription-usage</c> with <c>enforce</c> can claim capacity,
    /// because only the counter's atomic increment settles two callers wanting the same last unit.
    /// <list type="bullet">
    /// <item><c>authoritative</c> (the default, and what this endpoint has always done) reads the
    /// counters.</item>
    /// <item><c>projection</c> reads the published read model in one indexed query, falling back to
    /// the counters if nothing has been published for the subscription yet.</item>
    /// </list>
    /// Both modes return the identical <c>UsageResponse[]</c> body. How the read was served is
    /// reported in <c>X-Usage-Read-*</c> response headers rather than in the body, so opting into a
    /// mode cannot change the shape a consumer parses.
    /// </remarks>
    [HttpGet("current")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UsageResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UsageResponse>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UsageResponse>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrent(
        [FromQuery] string? organizationId,
        [FromQuery] string? readMode,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (!TryParseReadMode(readMode, out var mode))
        {
            return BadRequest(ApiResponse<IReadOnlyList<UsageResponse>>.Fail(
                "subscription_usage_read_mode_invalid",
                "readMode must be omitted, 'authoritative' or 'projection'.",
                correlationId));
        }

        var result = await _usage.ReadCurrentAsync(
            organizationId,
            mode,
            correlationId,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return SubscriptionOperationResult<IReadOnlyList<UsageResponse>>.Failure(
                    result.FailureKind,
                    result.ErrorCode!,
                    result.ErrorMessage!,
                    correlationId)
                .ToActionResult(correlationId);
        }

        Describe(result.Value!.Diagnostics);

        return SubscriptionOperationResult<IReadOnlyList<UsageResponse>>.Success(
                result.Value!.Items,
                correlationId)
            .ToActionResult(correlationId);
    }

    /// <summary>
    /// Accepts the mode by name, case-insensitively, and refuses anything else.
    /// </summary>
    /// <remarks>
    /// Refused rather than silently defaulted. A caller that misspells <c>projection</c> and is
    /// quietly served the authoritative path would measure the wrong thing and conclude the
    /// projection had no benefit.
    /// </remarks>
    private static bool TryParseReadMode(string? value, out UsageReadMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = UsageReadMode.Authoritative;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out mode) &&
               Enum.IsDefined(mode);
    }

    /// <summary>
    /// Puts the read diagnostics on the response headers.
    /// </summary>
    /// <remarks>
    /// Headers rather than the body, so both modes keep the identical array contract and no existing
    /// consumer of this endpoint sees a changed shape.
    /// </remarks>
    private void Describe(UsageReadDiagnostics diagnostics)
    {
        var headers = Response.Headers;

        headers["X-Usage-Read-Mode"] = diagnostics.RequestedMode.ToString();
        headers["X-Usage-Read-Source"] = diagnostics.ActualMode.ToString();
        headers["X-Usage-Read-Duration-Ms"] =
            diagnostics.DurationMs.ToString("F1", CultureInfo.InvariantCulture);
        headers["X-Usage-Read-Documents"] =
            diagnostics.DocumentCount.ToString(CultureInfo.InvariantCulture);
        headers["X-Usage-Read-Stale"] = diagnostics.Stale ? "true" : "false";
        // Named rather than implied by Source differing from Mode: "nothing published" and "only
        // some windows published" both fall back to the counters, and they mean different things to
        // whoever is watching.
        headers["X-Usage-Read-Fallback"] = diagnostics.Fallback.ToString();

        if (diagnostics.NewestProjectionAgeSeconds is { } age)
        {
            headers["X-Usage-Projection-Age-Seconds"] =
                age.ToString("F1", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Estimates the cost of additional metered usage, using the active subscription's own
    /// snapshotted terms and the same rating, discount and tax logic as the final usage invoice.
    /// </summary>
    /// <remarks>
    /// Advisory only: nothing here is recorded against the usage ledger and nothing here is
    /// charged. Usage recorded after <c>calculatedAtUtc</c> can change the eventual invoice — see
    /// <c>finalChargeDependsOnActualPeriodEndUsage</c> on the response.
    /// </remarks>
    [HttpPost("overage/preview")]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionUsageOveragePreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionUsageOveragePreviewResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionUsageOveragePreviewResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionUsageOveragePreviewResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionUsageOveragePreviewResponse>),
        StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PreviewOverage(
        [FromBody] PreviewUsageOverageRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _overagePreview.PreviewAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
