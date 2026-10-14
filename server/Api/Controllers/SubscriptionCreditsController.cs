using Api.Utilities;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Api.Controllers;

/// <summary>
/// Buying, granting, moving and listing a subscription's meter credits. See spec 002.
/// </summary>
/// <remarks>
/// Its own controller under the subscription's route rather than more actions on
/// <see cref="SubscriptionsController"/>, which is already the widest controller here and whose
/// every dependency each of these would otherwise be constructed with.
/// <para>
/// Every action, reads and previews included, requires <c>subscription::manage</c>. Buying spends
/// the organization's money and granting gives credits away for free, so for now the same people
/// who can change the plan are the only ones who can do either; a narrower permission for free
/// grants is a recorded deferred decision in the spec, not an oversight.
/// </para>
/// </remarks>
[ApiController]
[Route("subscriptions/{subscriptionId}/credits")]
public sealed class SubscriptionCreditsController : ControllerBase
{
    private const string Permission = "blocks-utilities::subscription::manage";

    private readonly ISubscriptionCreditService _credits;

    /// <summary>Takes the credit service, which owns every rule these actions forward to.</summary>
    public SubscriptionCreditsController(ISubscriptionCreditService credits) => _credits = credits;

    /// <summary>
    /// What buying credits would cost, and when they would expire. Changes nothing.
    /// </summary>
    /// <remarks>
    /// Checks everything the purchase does, in the same order, so a quote that comes back is a
    /// purchase that will go through at that price unless the card declines.
    /// </remarks>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(ApiResponse<CreditQuoteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CreditQuoteResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<CreditQuoteResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<CreditQuoteResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(Permission)]
    public async Task<IActionResult> Preview(
        string subscriptionId,
        [FromBody] CreditPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _credits.PreviewAsync(subscriptionId, request, correlationId, cancellationToken);
        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Buys credits: charges the stored card once and grants them.
    /// </summary>
    /// <remarks>
    /// <c>idempotencyKey</c> is required. Sending the same key and the same request again returns
    /// the first result with <c>replayed</c> set, and never charges a second time; the same key with
    /// a different request is refused. A declined card answers 422 and grants nothing.
    /// <para>
    /// The invoice is issued in the background once the payment settles, and appears in the
    /// subscription's invoice list.
    /// </para>
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreditPurchaseResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CreditPurchaseResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<CreditPurchaseResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<CreditPurchaseResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<CreditPurchaseResponse>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse<CreditPurchaseResponse>), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(Permission)]
    public async Task<IActionResult> Purchase(
        string subscriptionId,
        [FromBody] CreditPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _credits.PurchaseAsync(subscriptionId, request, correlationId, cancellationToken);
        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// A subscription's credit grants, newest first, with what is left of each.
    /// </summary>
    /// <remarks>
    /// <c>pool=true</c> lists only the shared pool, and is ignored when a seat is named.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CreditGrantResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CreditGrantResponse>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(Permission)]
    public async Task<IActionResult> ListGrants(
        string subscriptionId,
        [FromQuery] string? organizationId,
        [FromQuery] string? meterKey,
        [FromQuery] int? seatNumber,
        [FromQuery] bool pool,
        [FromQuery] CreditGrantStatus? status,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _credits.ListGrantsAsync(
            subscriptionId, organizationId, meterKey, seatNumber, pool, status, correlationId, cancellationToken);
        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Gives credits without a charge. A reason is required and kept with the grant.
    /// </summary>
    [HttpPost("grants")]
    [ProducesResponseType(typeof(ApiResponse<CreditGrantResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CreditGrantResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<CreditGrantResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<CreditGrantResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(Permission)]
    public async Task<IActionResult> Grant(
        string subscriptionId,
        [FromBody] GrantCreditsRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _credits.GrantAsync(subscriptionId, request, correlationId, cancellationToken);
        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Moves unused credits between the shared pool and a seat, or between two seats.
    /// </summary>
    /// <remarks>
    /// The credits that would lapse soonest are moved first, and each keeps its own expiry. Refused
    /// whole when the giving bucket holds fewer unused credits than asked for.
    /// </remarks>
    [HttpPost("moves")]
    [ProducesResponseType(typeof(ApiResponse<CreditMoveResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CreditMoveResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<CreditMoveResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<CreditMoveResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(Permission)]
    public async Task<IActionResult> Move(
        string subscriptionId,
        [FromBody] MoveCreditsRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _credits.MoveAsync(subscriptionId, request, correlationId, cancellationToken);
        return result.ToActionResult(correlationId);
    }

    /// <summary>A subscription's credit purchases, newest first, including failed ones.</summary>
    [HttpGet("purchases")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CreditPurchaseResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CreditPurchaseResponse>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(Permission)]
    public async Task<IActionResult> ListPurchases(
        string subscriptionId,
        [FromQuery] string? organizationId,
        [FromQuery] CreditPurchaseStatus? status,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _credits.ListPurchasesAsync(
            subscriptionId, organizationId, status, correlationId, cancellationToken);
        return result.ToActionResult(correlationId);
    }
}
