using Api.Utilities;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Api.Controllers;

/// <summary>
/// Standalone credit offers (spec 005): what an organization pays for a meter's credits bought
/// outside any plan, one offer per meter per scope.
/// </summary>
/// <remarks>
/// Authored under the plan permissions, like the catalogue. Buying from an offer is on
/// <see cref="OrganizationCreditsController"/>, under the subscription manage permission, because
/// that spends the organization's money.
/// </remarks>
[ApiController]
[Route("credit-offers")]
public sealed class CreditOffersController : ControllerBase
{
    private const string ReadPermission = "blocks-utilities::subscription-plan::read";
    private const string ManagePermission = "blocks-utilities::subscription-plan::manage";

    private readonly ICreditOfferService _offers;

    public CreditOffersController(ICreditOfferService offers) => _offers = offers;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CreditOfferResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(ReadPermission)]
    public async Task<IActionResult> List(
        [FromQuery] string? organizationId,
        [FromQuery] string? meterKey,
        [FromQuery] bool? enabled,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _offers.ListAsync(organizationId, meterKey, enabled, correlationId, cancellationToken))
            .ToActionResult(correlationId);
    }

    /// <summary>The offer the named scope is served for one meter: its override when it has one, otherwise the tenant's.</summary>
    [HttpGet("{meterKey}")]
    [ProducesResponseType(typeof(ApiResponse<CreditOfferResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CreditOfferResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(ReadPermission)]
    public async Task<IActionResult> Get(string meterKey, [FromQuery] string? organizationId, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _offers.GetAsync(meterKey, organizationId, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreditOfferResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CreditOfferResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<CreditOfferResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<CreditOfferResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(ManagePermission)]
    public async Task<IActionResult> Create([FromBody] CreditOfferRequest request, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _offers.CreateAsync(request, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    /// <summary>Replaces the offer's terms at exactly the named scope and raises its version.</summary>
    [HttpPut("{meterKey}")]
    [ProducesResponseType(typeof(ApiResponse<CreditOfferResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CreditOfferResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<CreditOfferResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<CreditOfferResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(ManagePermission)]
    public async Task<IActionResult> Update(
        string meterKey,
        [FromBody] CreditOfferRequest request,
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _offers.UpdateAsync(meterKey, request, organizationId, correlationId, cancellationToken))
            .ToActionResult(correlationId);
    }
}
