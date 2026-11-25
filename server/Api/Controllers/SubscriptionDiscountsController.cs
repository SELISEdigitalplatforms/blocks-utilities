using Api.Utilities;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Api.Controllers;

/// <remarks>
/// Every action here manages the discount/campaign catalogue -- authoring, editing, retiring and
/// listing it -- never redeeming one. A buyer applying a code at checkout goes through the
/// subscription endpoints instead. Catalogue management currently requires authentication; a
/// dedicated campaign-management permission can be restored once the identity provider exposes
/// and assigns it consistently.
/// </remarks>
[ApiController, Route("subscription-discounts")]
public sealed class SubscriptionDiscountsController : ControllerBase
{
    private readonly IDiscountCatalogueService _catalogue;
    public SubscriptionDiscountsController(IDiscountCatalogueService catalogue) => _catalogue = catalogue;

    [HttpGet]
    [ProtectedEndPoint("blocks-utilities::subscription-discount::read")]
    public async Task<IActionResult> List([FromQuery] string? organizationId, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.ListAsync(organizationId, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    [HttpGet("{discountId}")]
    [ProtectedEndPoint("blocks-utilities::subscription-discount::read")]
    public async Task<IActionResult> Get(string discountId, [FromQuery] string? organizationId, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.GetAsync(discountId, organizationId, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    /// <summary>Who created or changed this discount, and when, newest first. <c>limit</c> defaults to 100, at most 500.</summary>
    [HttpGet("{discountId}/audit")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubscriptionAuditEventResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubscriptionAuditEventResponse>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint("blocks-utilities::subscription-discount::read")]
    public async Task<IActionResult> GetAudit(
        string discountId, [FromQuery] string? organizationId, [FromQuery] int limit, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.GetAuditAsync(discountId, organizationId, limit, correlationId, cancellationToken))
            .ToActionResult(correlationId);
    }

    [HttpPost]
    [ProtectedEndPoint("blocks-utilities::subscription-discount::manage")]
    public async Task<IActionResult> Create([FromBody] CreateDiscountRequest request, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.CreateAsync(request, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    [HttpPut("{discountId}")]
    [ProtectedEndPoint("blocks-utilities::subscription-discount::manage")]
    public async Task<IActionResult> Update(
        string discountId,
        [FromBody] UpdateDiscountRequest request,
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.UpdateAsync(discountId, request, organizationId, correlationId, cancellationToken))
            .ToActionResult(correlationId);
    }

    [HttpPut("{discountId}/archive")]
    [ProtectedEndPoint("blocks-utilities::subscription-discount::manage")]
    public async Task<IActionResult> Archive(string discountId, [FromQuery] string? organizationId, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.ArchiveAsync(discountId, organizationId, correlationId, cancellationToken)).ToActionResult(correlationId);
    }
}
