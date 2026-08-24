using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Payment.DomainService.Responses;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;
using Subscription.DomainService.Simulation;
using Subscription.DomainService.Utilities;

namespace Api.Controllers;

/// <summary>
/// The subscription simulation test harness. Console-only, permission-gated, and unavailable
/// wherever <c>SubscriptionSimulation:Enabled</c> is not explicitly turned on.
/// </summary>
/// <remarks>
/// Never rely on this being hidden from a UI: every action here re-checks
/// <see cref="SubscriptionSimulationOptions.Enabled"/> on every request, because the
/// configuration this reads can be changed at runtime without a restart.
/// </remarks>
[ApiController]
[Authorize]
[Route("subscription-simulation")]
public sealed class SubscriptionSimulationController : ControllerBase
{
    private readonly ISubscriptionSimulationService _simulation;
    private readonly IOptionsMonitor<SubscriptionSimulationOptions> _options;

    public SubscriptionSimulationController(
        ISubscriptionSimulationService simulation,
        IOptionsMonitor<SubscriptionSimulationOptions> options)
    {
        _simulation = simulation;
        _options = options;
    }

    /// <summary>A complete, read-only snapshot of one subscription.</summary>
    /// <remarks>
    /// Never returns a stored payment method id, a provider customer id, a checkout URL, or an
    /// outbox event's raw payload.
    /// </remarks>
    [HttpGet("subscriptions/{subscriptionId}/state")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationStateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationStateResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetState(
        string subscriptionId,
        [FromQuery] string? organizationId,
        [FromQuery] int auditLimit,
        [FromQuery] int paymentLimit,
        [FromQuery] bool includeBackgroundWork,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (Disabled<SubscriptionSimulationStateResponse>(correlationId) is { } disabled)
        {
            return disabled;
        }

        var result = await _simulation.GetStateAsync(
            subscriptionId,
            organizationId,
            auditLimit <= 0 ? 100 : auditLimit,
            paymentLimit <= 0 ? 100 : paymentLimit,
            includeBackgroundWork,
            correlationId,
            cancellationToken);

        return Forbidden(result, correlationId) ?? result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Simulates a successful outcome for the subscription's outstanding charge — the first
    /// charge or a renewal — through the same settlement path a real provider confirmation
    /// would take.
    /// </summary>
    [HttpPost("subscriptions/{subscriptionId}/mark-payment-succeeded")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkPaymentSucceeded(
        string subscriptionId,
        [FromBody] MarkPaymentSucceededRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (Disabled<SubscriptionSimulationActionResponse>(correlationId) is { } disabled)
        {
            return disabled;
        }

        var result = await _simulation.MarkPaymentSucceededAsync(
            subscriptionId, request, correlationId, cancellationToken);

        return Forbidden(result, correlationId) ?? result.ToActionResult(correlationId);
    }

    /// <summary>Simulates a failed outcome, for the same charge <c>mark-payment-succeeded</c> would settle.</summary>
    [HttpPost("subscriptions/{subscriptionId}/mark-payment-failed")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkPaymentFailed(
        string subscriptionId,
        [FromBody] MarkPaymentFailedRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (Disabled<SubscriptionSimulationActionResponse>(correlationId) is { } disabled)
        {
            return disabled;
        }

        var result = await _simulation.MarkPaymentFailedAsync(
            subscriptionId, request, correlationId, cancellationToken);

        return Forbidden(result, correlationId) ?? result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Forces an immediate renewal attempt, with a scripted payment outcome, without waiting for
    /// the fee schedule's own due date.
    /// </summary>
    [HttpPost("subscriptions/{subscriptionId}/advance-renewal")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AdvanceRenewal(
        string subscriptionId,
        [FromBody] AdvanceRenewalRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (Disabled<SubscriptionSimulationActionResponse>(correlationId) is { } disabled)
        {
            return disabled;
        }

        var result = await _simulation.AdvanceRenewalAsync(
            subscriptionId, request, correlationId, cancellationToken);

        return Forbidden(result, correlationId) ?? result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Closes the subscription's current usage period now, prices any overage, and — unless
    /// told otherwise — charges it with a scripted payment outcome.
    /// </summary>
    [HttpPost("subscriptions/{subscriptionId}/close-usage-period")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionSimulationActionResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CloseUsagePeriod(
        string subscriptionId,
        [FromBody] CloseUsagePeriodRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        if (Disabled<SubscriptionSimulationActionResponse>(correlationId) is { } disabled)
        {
            return disabled;
        }

        var result = await _simulation.CloseUsagePeriodAsync(
            subscriptionId, request, correlationId, cancellationToken);

        return Forbidden(result, correlationId) ?? result.ToActionResult(correlationId);
    }

    private IActionResult? Disabled<T>(string correlationId) =>
        _options.CurrentValue.Enabled
            ? null
            : NotFound(ApiResponse<T>.Fail(
                "subscription_simulation_disabled",
                "The subscription simulation harness is not enabled in this environment.",
                correlationId));

    /// <summary>
    /// <see cref="Payment.DomainService.Enums.PaymentFailureKind"/> has no <c>Forbidden</c> — the
    /// shared status-code mapping every other subscription result reuses would otherwise report
    /// this as a 404, indistinguishable from the harness being disabled. Named separately so an
    /// authenticated caller who simply lacks the permission sees why, rather than being told a
    /// real subscription is "disabled".
    /// </summary>
    private static IActionResult? Forbidden<T>(
        SubscriptionOperationResult<T> result,
        string correlationId) =>
        result is { IsSuccess: false, ErrorCode: "subscription_simulation_forbidden" }
            ? new ObjectResult(ApiResponse<T>.Fail(
                result.ErrorCode,
                result.ErrorMessage ?? "This caller may not use the subscription simulation harness.",
                correlationId))
            {
                StatusCode = StatusCodes.Status403Forbidden
            }
            : null;
}
