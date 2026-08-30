using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;

namespace Api.Controllers;

/// <remarks>
/// Every action here manages the discount/campaign catalogue -- authoring, editing, retiring and
/// listing it -- never redeeming one. A buyer applying a code at checkout goes through the
/// subscription endpoints instead. Catalogue management currently requires authentication; a
/// dedicated campaign-management permission can be restored once the identity provider exposes
/// and assigns it consistently.
/// </remarks>
[ApiController, Authorize, Route("subscription-discounts")]
public sealed class SubscriptionDiscountsController : ControllerBase
{
    private readonly IDiscountCatalogueService _catalogue;
    public SubscriptionDiscountsController(IDiscountCatalogueService catalogue) => _catalogue = catalogue;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? organizationId, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.ListAsync(organizationId, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    [HttpGet("{discountId}")]
    public async Task<IActionResult> Get(string discountId, [FromQuery] string? organizationId, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.GetAsync(discountId, organizationId, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDiscountRequest request, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.CreateAsync(request, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    [HttpPut("{discountId}")]
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
    public async Task<IActionResult> Archive(string discountId, [FromQuery] string? organizationId, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.ArchiveAsync(discountId, organizationId, correlationId, cancellationToken)).ToActionResult(correlationId);
    }
}
