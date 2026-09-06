using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Api.Controllers;

/// <summary>Buyer-facing, read-only validation and pricing of a discount code.</summary>
[ApiController, Authorize, Route("subscription-discounts")]
public sealed class SubscriptionDiscountPreviewController : ControllerBase
{
    private readonly ISubscriptionCreationService _creation;
    private readonly ISubscriptionContextResolver _contextResolver;

    public SubscriptionDiscountPreviewController(
        ISubscriptionCreationService creation,
        ISubscriptionContextResolver contextResolver)
    {
        _creation = creation;
        _contextResolver = contextResolver;
    }

    [HttpPost("preview")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionDiscountPreviewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        [FromBody] CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var resolution = await _contextResolver.ResolveAsync(
            correlationId, request.OrganizationId, cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<SubscriptionDiscountPreviewResponse>(correlationId)
                .ToActionResult(correlationId);
        }

        return (await _creation.PreviewDiscountAsync(
                request, resolution.Context!, correlationId, cancellationToken))
            .ToActionResult(correlationId);
    }
}
