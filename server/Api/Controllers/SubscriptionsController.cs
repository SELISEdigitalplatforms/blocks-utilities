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
/// An organization's own subscription. Served under <c>/api/subscriptions</c>.
/// </summary>
/// <remarks>
/// No endpoint here takes an organization. Every one resolves it from the authenticated
/// caller, because an identifier in a URL is something anyone can change.
/// </remarks>
[ApiController]
[Authorize]
[Route("subscriptions")]
public sealed class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionCheckoutService _checkout;

    public SubscriptionsController(ISubscriptionCheckoutService checkout) =>
        _checkout = checkout;

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Subscribe(
        [FromBody] CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _checkout.SubscribeAsync(
            request,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// The caller's own subscription.
    /// </summary>
    /// <remarks>
    /// Immediately after paying this may still report <c>Incomplete</c>: the shopper's browser
    /// usually returns before the provider's webhook lands, and only the webhook is treated as
    /// proof that money moved. Clients should expect a short pending state rather than assume
    /// something failed.
    /// </remarks>
    [HttpGet("current")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _checkout.GetCurrentAsync(correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
