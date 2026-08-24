using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Subscription.DomainService.Scheduling;

namespace Api.Controllers;

/// <summary>
/// Background work the scheduler gave up on, and what an operator can do about it.
/// </summary>
/// <remarks>
/// Served under <c>/api/subscription-background-work</c>. Every endpoint answers for the
/// authenticated caller's own tenant: the work item id is a platform-wide identifier, so without
/// that scoping anyone could act on another tenant's work simply by naming it.
/// <para>
/// This exists because the alternative was editing the database. Requeueing by hand means clearing
/// a status, a lease and an attempt count together and getting all three right; miss one and the
/// item is either unclaimable or dead-letters again immediately, both of which look like nothing
/// happened.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("subscription-background-work")]
public sealed class SubscriptionBackgroundWorkController : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaximumLimit = 200;

    private readonly ISubscriptionWorkRecoveryService _recovery;

    public SubscriptionBackgroundWorkController(ISubscriptionWorkRecoveryService recovery) =>
        _recovery = recovery;

    /// <summary>
    /// Work that has been given up on, newest decision first.
    /// </summary>
    /// <remarks>
    /// Each entry carries what the work was, what it was about, how many attempts it had, the error
    /// classification that stopped it, and how long it has been due. That last number is the one to
    /// look at before requeueing anything: a month-old renewal is rarely what somebody means to
    /// retry.
    /// </remarks>
    [HttpGet("dead-letters")]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<DeadLetteredWorkResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<DeadLetteredWorkResponse>>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ListDeadLetters(
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _recovery.ListAsync(
            Math.Clamp(limit ?? DefaultLimit, 1, MaximumLimit),
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Puts one back in the queue.
    /// </summary>
    /// <remarks>
    /// Status, attempts and lease are cleared in a single write, so the item is never reachable
    /// half-recovered. It does not decide that the work is still due — the handler re-reads the
    /// subscription and decides that, which is the difference between an operator saying "try
    /// again" and an operator saying "charge this".
    /// </remarks>
    [HttpPost("dead-letters/{workItemId}/requeue")]
    [ProducesResponseType(typeof(ApiResponse<DeadLetteredWorkResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<DeadLetteredWorkResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<DeadLetteredWorkResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<DeadLetteredWorkResponse>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Requeue(
        string workItemId,
        [FromBody] WorkRecoveryDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _recovery.RequeueAsync(
            workItemId,
            request?.Reason ?? string.Empty,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Sets one aside for good.
    /// </summary>
    /// <remarks>
    /// A reason is required rather than optional: an abandoned dead letter without one is a decision
    /// nobody can review, and reviewing them is the only reason to keep them. Abandoned work is not
    /// purged — the reason is part of the record.
    /// </remarks>
    [HttpPost("dead-letters/{workItemId}/abandon")]
    [ProducesResponseType(typeof(ApiResponse<DeadLetteredWorkResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<DeadLetteredWorkResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<DeadLetteredWorkResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<DeadLetteredWorkResponse>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Abandon(
        string workItemId,
        [FromBody] WorkRecoveryDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _recovery.AbandonAsync(
            workItemId,
            request?.Reason ?? string.Empty,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }
}

/// <summary>Why an operator is doing this. Recorded against their identity.</summary>
public sealed class WorkRecoveryDecisionRequest
{
    public string Reason { get; set; } = string.Empty;
}
