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

    public SubscriptionUsageController(IUsageRecordingService usage) => _usage = usage;

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
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _usage.GetCurrentUsageAsync(correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
