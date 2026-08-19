using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;

namespace Api.Controllers;

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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDiscountRequest request, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.CreateAsync(request, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    [HttpPut("{discountId}/archive")]
    public async Task<IActionResult> Archive(string discountId, [FromQuery] string? organizationId, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.ArchiveAsync(discountId, organizationId, correlationId, cancellationToken)).ToActionResult(correlationId);
    }
}
