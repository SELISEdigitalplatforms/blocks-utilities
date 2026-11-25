using Api.Utilities;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace Api.Controllers;

/// <summary>Who did what to payments, providers and saved cards, and how it ended.</summary>
[ApiController]
[Route("payment-audit")]
public sealed class PaymentAuditController : ControllerBase
{
    private readonly IPaymentAuditTrail _audit;

    public PaymentAuditController(IPaymentAuditTrail audit) => _audit = audit;

    /// <summary>
    /// Newest first. The caller's own organization's events; the whole tenant's for the console or
    /// a tenant-wide integration. Narrow with <c>subjectType</c> (<c>Payment</c>,
    /// <c>PaymentProvider</c>, <c>PaymentMethod</c>, <c>Tenant</c>) and <c>subjectId</c>.
    /// <c>limit</c> defaults to 100, at most 500. Actor ids stay in the database.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PaymentAuditEventResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint("blocks-utilities::payment::read")]
    public async Task<IActionResult> List(
        [FromQuery] string? subjectType,
        [FromQuery] string? subjectId,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var events = await _audit.ListAsync(subjectType, subjectId, limit, correlationId, cancellationToken);

        if (events is null)
        {
            return Unauthorized(ApiResponse<object>.Fail(
                "payment_context_unresolved",
                "The caller could not be identified.",
                correlationId));
        }

        return Ok(ApiResponse<IReadOnlyList<PaymentAuditEventResponse>>.Ok(
            events.Select(PaymentAuditEventResponse.From).ToList(),
            correlationId));
    }
}
