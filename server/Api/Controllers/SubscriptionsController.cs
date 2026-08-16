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
/// Every endpoint resolves its organization from the authenticated caller. A caller may also
/// name one explicitly, but it is honored only for the platform console — see the module
/// README's "Console organization override" section — so an identifier here is not simply
/// something anyone can change.
/// </remarks>
[ApiController]
[Authorize]
[Route("subscriptions")]
public sealed class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionCheckoutService _checkout;
    private readonly ISubscriptionCancellationService _cancellation;
    private readonly ISubscriptionPlanChangeService _planChange;

    public SubscriptionsController(
        ISubscriptionCheckoutService checkout,
        ISubscriptionCancellationService cancellation,
        ISubscriptionPlanChangeService planChange)
    {
        _checkout = checkout;
        _cancellation = cancellation;
        _planChange = planChange;
    }

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
    public async Task<IActionResult> GetCurrent(
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _checkout.GetCurrentAsync(
            organizationId,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Cancels a subscription.
    /// </summary>
    /// <remarks>
    /// By default the subscription keeps granting until the period already paid for runs out,
    /// and simply stops renewing. Pass <c>immediately</c> only where the customer is entitled
    /// to stop at once.
    /// </remarks>
    [HttpDelete("{subscriptionId}")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Cancel(
        string subscriptionId,
        [FromQuery] bool immediately,
        [FromQuery] string? reason,
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _cancellation.CancelAsync(
            subscriptionId,
            immediately,
            reason,
            organizationId,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Moves the subscription to a different price, mid-period.
    /// </summary>
    /// <remarks>
    /// An upgrade is charged immediately for the prorated difference; a downgrade is credited
    /// toward future renewals rather than refunded. A trial has paid nothing yet, so its plan
    /// simply swaps with no charge or credit either way.
    /// </remarks>
    [HttpPut("{subscriptionId}/plan")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePlan(
        string subscriptionId,
        [FromBody] ChangeSubscriptionPlanRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _planChange.ChangePlanAsync(
            subscriptionId,
            request,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
