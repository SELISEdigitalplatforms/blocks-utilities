using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Payment.DomainService.Responses;
using Subscription.DomainService.Responses;
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

        if (!_options.CurrentValue.Enabled)
        {
            return NotFound(ApiResponse<SubscriptionSimulationStateResponse>.Fail(
                "subscription_simulation_disabled",
                "The subscription simulation harness is not enabled in this environment.",
                correlationId));
        }

        var result = await _simulation.GetStateAsync(
            subscriptionId,
            organizationId,
            auditLimit <= 0 ? 100 : auditLimit,
            paymentLimit <= 0 ? 100 : paymentLimit,
            includeBackgroundWork,
            correlationId,
            cancellationToken);

        // PaymentFailureKind has no Forbidden — the shared status-code mapping this reuses for
        // every other subscription result would otherwise report this as a 404, indistinguishable
        // from the harness being disabled. Named separately so an authorized administrator who
        // simply lacks the permission sees why, rather than being told a real subscription is
        // "disabled".
        if (result is { IsSuccess: false, ErrorCode: "subscription_simulation_forbidden" })
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<SubscriptionSimulationStateResponse>.Fail(
                    result.ErrorCode,
                    result.ErrorMessage ?? "This caller may not use the subscription simulation harness.",
                    correlationId));
        }

        return result.ToActionResult(correlationId);
    }
}
