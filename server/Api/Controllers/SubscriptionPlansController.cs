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
/// What a tenant sells. Served under <c>/api/subscription-plans</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("subscription-plans")]
public sealed class SubscriptionPlansController : ControllerBase
{
    private readonly IPlanCatalogueService _catalogue;

    public SubscriptionPlansController(IPlanCatalogueService catalogue) =>
        _catalogue = catalogue;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PlanResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PlanResponse>>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ListPlans(
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _catalogue.ListPlansAsync(
            organizationId,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    [HttpGet("{planId}")]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPlan(
        string planId,
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _catalogue.GetPlanAsync(
            planId,
            organizationId,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreatePlan(
        [FromBody] CreatePlanRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _catalogue.CreatePlanAsync(
            request,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Rewrites what a plan sells. Refused with 409 once anything has subscribed to it — a
    /// subscription bills from its own copy of the plan's terms, which an edit cannot reach.
    /// </summary>
    [HttpPut("{planId}")]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdatePlan(
        string planId,
        [FromBody] UpdatePlanRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _catalogue.UpdatePlanAsync(
            planId,
            request,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    [HttpPost("prices")]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreatePrice(
        [FromBody] CreatePriceRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _catalogue.CreatePriceAsync(
            request,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Takes a price off the menu.
    /// </summary>
    /// <remarks>
    /// Nothing already sold on it changes: a subscription bills from the price snapshot copied
    /// onto it at signup and never reads the catalogue again. What stops is selling it — a new
    /// subscription or a plan change naming this price is refused from here on.
    /// <para>
    /// There is deliberately no way to edit or delete a price. A price identifier is what every
    /// subscription records having been sold on, so it is superseded by adding another and
    /// retiring this one, never rewritten underneath the subscriptions that reference it.
    /// </para>
    /// </remarks>
    [HttpPut("prices/{priceId}/archive")]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ArchivePrice(
        string priceId,
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _catalogue.ArchivePriceAsync(
            priceId,
            organizationId,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
