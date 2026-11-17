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
/// The meter catalogue (spec 005): the meters a tenant counts, named once so plans and credit
/// offers refer to the same thing.
/// </summary>
/// <remarks>
/// Gated by the plan authoring permissions, because the same people author both and a meter is
/// half a plan. <c>organizationId</c> is honoured only for the platform console and selects the
/// organization override being read or written; omitted, the console addresses the tenant-wide
/// entry.
/// </remarks>
[ApiController]
[Route("meter-catalogue")]
public sealed class MeterCatalogueController : ControllerBase
{
    private const string ReadPermission = "blocks-utilities::subscription-plan::read";
    private const string ManagePermission = "blocks-utilities::subscription-plan::manage";

    private readonly IMeterCatalogueService _catalogue;

    public MeterCatalogueController(IMeterCatalogueService catalogue) => _catalogue = catalogue;

    /// <summary>The tenant's meters plus the named organization's overrides.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MeterCatalogueEntryResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(ReadPermission)]
    public async Task<IActionResult> List([FromQuery] string? organizationId, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.ListAsync(organizationId, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    /// <summary>One meter as seen from the named scope: its override when it has one, otherwise the tenant's.</summary>
    [HttpGet("{meterKey}")]
    [ProducesResponseType(typeof(ApiResponse<MeterCatalogueEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MeterCatalogueEntryResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(ReadPermission)]
    public async Task<IActionResult> Get(string meterKey, [FromQuery] string? organizationId, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.GetAsync(meterKey, organizationId, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<MeterCatalogueEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MeterCatalogueEntryResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MeterCatalogueEntryResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(ManagePermission)]
    public async Task<IActionResult> Create([FromBody] CreateMeterCatalogueEntryRequest request, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.CreateAsync(request, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    /// <summary>Edits, or archives by setting <c>status</c> to Archived, the entry at exactly the named scope.</summary>
    [HttpPut("{meterKey}")]
    [ProducesResponseType(typeof(ApiResponse<MeterCatalogueEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MeterCatalogueEntryResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MeterCatalogueEntryResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<MeterCatalogueEntryResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(ManagePermission)]
    public async Task<IActionResult> Update(
        string meterKey,
        [FromBody] UpdateMeterCatalogueEntryRequest request,
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _catalogue.UpdateAsync(meterKey, request, organizationId, correlationId, cancellationToken))
            .ToActionResult(correlationId);
    }
}
