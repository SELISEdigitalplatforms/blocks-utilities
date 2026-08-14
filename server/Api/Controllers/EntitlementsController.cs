using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Api.Controllers;

/// <summary>
/// What the caller's organization may do. Served under <c>/api/entitlements</c>.
/// </summary>
/// <remarks>
/// The hot path: called on every gated action, answered from our own data, and never dependent
/// on a payment provider being reachable.
/// <para>
/// Advisory for anything metered. Two callers can both be told they have one unit left; only
/// recording the usage settles which of them actually did.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("entitlements")]
public sealed class EntitlementsController : ControllerBase
{
    private readonly IEntitlementService _entitlements;

    public EntitlementsController(IEntitlementService entitlements) =>
        _entitlements = entitlements;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<EntitlementSnapshotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<EntitlementSnapshotResponse>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool fresh,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _entitlements.GetAsync(fresh, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    [HttpGet("{entitlementKey}")]
    [ProducesResponseType(typeof(ApiResponse<EntitlementResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(
        string entitlementKey,
        [FromQuery] bool fresh,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _entitlements.GetAsync(
            entitlementKey,
            fresh,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
