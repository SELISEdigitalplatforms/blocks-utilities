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
/// The legal identity this tenant issues its invoices and credit notes under.
/// </summary>
/// <remarks>
/// The selling counterpart of <see cref="SubscriptionBillingProfileController"/>. Tenant-scoped and
/// writable by the platform console alone: an invoice names a seller in law, and a subscriber able to
/// set that could have their own invoices issued under a company of their choosing.
/// <para>
/// Readable by any authenticated caller in the tenant, because it is printed on every document they
/// have already been sent.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("subscription-merchant-profile")]
public sealed class SubscriptionMerchantProfileController : ControllerBase
{
    private readonly ISubscriptionMerchantProfileService _profiles;

    public SubscriptionMerchantProfileController(ISubscriptionMerchantProfileService profiles) =>
        _profiles = profiles;

    /// <summary>
    /// Reads the tenant's merchant profile.
    /// </summary>
    /// <remarks>
    /// A tenant that has never set one gets the configured fallback with
    /// <c>isInheritedFromConfiguration</c> true, rather than a 404 — the fallback is what its
    /// documents are actually being issued under, so that is what a console needs to show.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionMerchantProfileResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _profiles.GetAsync(correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Replaces the tenant's merchant profile. Platform console only.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionMerchantProfileResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionMerchantProfileResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateMerchantProfileRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _profiles.UpdateAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
