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
/// The identity an organization's invoices and credit notes are addressed to.
/// </summary>
/// <remarks>
/// Its own controller rather than a pair of endpoints on the subscription, because the profile
/// belongs to the organization and outlives any one subscription: it is filled in before the first
/// one starts, survives cancellation, and is what the next one is invoiced against.
/// <para>
/// Every value here is copied onto each document as it is issued. Editing the profile changes what
/// future documents say and never what an issued one says — see the module README's "Financial
/// documents" section.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("subscription-billing-profile")]
public sealed class SubscriptionBillingProfileController : ControllerBase
{
    private readonly ISubscriptionBillingProfileService _profiles;

    public SubscriptionBillingProfileController(ISubscriptionBillingProfileService profiles) =>
        _profiles = profiles;

    /// <summary>
    /// Reads the calling organization's billing profile.
    /// </summary>
    /// <remarks>
    /// An organization that has never filled one in gets an empty profile rather than a 404, with
    /// <c>missingFields</c> naming what is still needed. A client rendering a form wants the same
    /// shape either way.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionBillingProfileResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _profiles.GetAsync(organizationId, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Replaces the calling organization's billing profile.
    /// </summary>
    /// <remarks>
    /// A whole-profile write rather than a patch: the fields describe one legal identity, and editing
    /// them one at a time invites a new street beside an old city on the next document issued.
    /// </remarks>
    [HttpPut]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionBillingProfileResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionBillingProfileResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateBillingProfileRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _profiles.UpdateAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
