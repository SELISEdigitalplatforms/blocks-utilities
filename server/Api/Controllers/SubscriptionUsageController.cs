using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
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

    [HttpGet("current")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UsageResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UsageResponse>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrent(
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _usage.GetCurrentUsageAsync(
            organizationId,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
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
