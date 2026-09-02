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
        [FromQuery] string? status,
        [FromQuery] string? familyCode,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        // Parsed here rather than bound as an enum, so an unrecognised value is a named validation
        // failure instead of the framework's own model-binding error — and so that omitting the
        // parameter keeps meaning Active, which is what every subscriber-facing caller sends.
        if (!TryParseCatalogueFilter(status, out var filter))
        {
            return BadRequest(ApiResponse<PlanResponse>.Fail(
                "subscription_plan_status_invalid",
                "Filter plans by Active, Archived or All.",
                correlationId));
        }

        // Passed through as given. Omitting it lists every family, exactly as before; a family
        // nobody authored is an empty list rather than a refusal, because a listing endpoint
        // reports what is there and there is nothing malformed about asking after a family that
        // holds nothing.
        var result = await _catalogue.ListPlansAsync(
            organizationId,
            correlationId,
            cancellationToken,
            filter,
            familyCode);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Reads the catalogue filter from the query string. Absent and empty both mean
    /// <see cref="PlanCatalogueFilter.Active"/>; anything unrecognised is rejected rather than
    /// quietly treated as the default, since a caller asking for Archived and silently receiving
    /// Active would be told a plan does not exist when it does.
    /// </summary>
    private static bool TryParseCatalogueFilter(string? status, out PlanCatalogueFilter filter)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            filter = PlanCatalogueFilter.Active;

            return true;
        }

        // Draft is a real member of the enum and deliberately not accepted: it appears in no
        // catalogue view, so honouring it would promise a listing that is always empty.
        return status.Trim() switch
        {
            var value when value.Equals(
                nameof(PlanCatalogueFilter.Active), StringComparison.OrdinalIgnoreCase) =>
                Accept(PlanCatalogueFilter.Active, out filter),
            var value when value.Equals(
                nameof(PlanCatalogueFilter.Archived), StringComparison.OrdinalIgnoreCase) =>
                Accept(PlanCatalogueFilter.Archived, out filter),
            var value when value.Equals(
                nameof(PlanCatalogueFilter.All), StringComparison.OrdinalIgnoreCase) =>
                Accept(PlanCatalogueFilter.All, out filter),
            _ => Reject(out filter)
        };

        static bool Accept(PlanCatalogueFilter value, out PlanCatalogueFilter filter)
        {
            filter = value;

            return true;
        }

        static bool Reject(out PlanCatalogueFilter filter)
        {
            filter = PlanCatalogueFilter.Active;

            return false;
        }
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
        [FromBody] CreatePriceRequest? request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (request is null)
        {
            return BadRequest(ApiResponse<PlanResponse>.Fail(
                "subscription_price_request_required",
                "A price request body is required.",
                correlationId));
        }

        var result = await _catalogue.CreatePriceAsync(
            request,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Sets or clears the automatic discount on a price. Future subscriptions and future moves onto
    /// this price only — nobody already on it is repriced.
    /// </summary>
    [HttpPut("prices/{priceId}/discount")]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdatePriceDiscount(
        string priceId,
        [FromBody] UpdatePriceDiscountRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _catalogue.UpdatePriceDiscountAsync(
            priceId,
            request,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    [HttpPut("prices/{priceId}/tax")]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdatePriceTax(
        string priceId,
        [FromBody] UpdatePriceTaxRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _catalogue.UpdatePriceTaxAsync(
            priceId,
            request,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Takes a plan off the menu, permanently.
    /// </summary>
    /// <remarks>
    /// Existing subscribers are unaffected and none of the plan's prices is rewritten: a
    /// subscription bills from the snapshot copied onto it when it was sold. Renewal, usage
    /// rating, entitlements, invoicing and cancellation all continue. What stops is selling, and
    /// every further change to the plan or its prices.
    /// <para>
    /// A <c>PUT</c> because it names the state the plan should be in rather than an event, and
    /// because repeating it is safe: a second call returns the archived plan without writing
    /// again. There is no restore — a replacement is made by duplicating the plan.
    /// </para>
    /// </remarks>
    [HttpPut("{planId}/archive")]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<PlanResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ArchivePlan(
        string planId,
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _catalogue.ArchivePlanAsync(
            planId,
            organizationId,
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
