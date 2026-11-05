using Api.Utilities;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Api.Controllers;

/// <summary>
/// Buying, granting and listing the standalone credits an organization holds outside any
/// subscription, and the invoices those purchases produced. See spec 005.
/// </summary>
/// <remarks>
/// Its own controller beside <see cref="SubscriptionCreditsController"/> rather than more routes
/// under a subscription, because there may be no subscription: that is the point. The same
/// permission as a subscription purchase, for the same reason — buying spends the organization's
/// money and granting gives credits away. <c>organizationId</c> is honoured only for the platform
/// console; everyone else acts on their own organization.
/// </remarks>
[ApiController]
[Route("organization-credits")]
public sealed class OrganizationCreditsController : ControllerBase
{
    private const string Permission = "blocks-utilities::subscription::manage";

    private readonly ISubscriptionCreditService _credits;
    private readonly ISubscriptionFinancialDocumentHistoryService _documents;

    public OrganizationCreditsController(
        ISubscriptionCreditService credits,
        ISubscriptionFinancialDocumentHistoryService documents)
    {
        _credits = credits;
        _documents = documents;
    }

    /// <summary>What buying from the organization's offer would cost, in the currency named. Changes nothing.</summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(ApiResponse<CreditQuoteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CreditQuoteResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<CreditQuoteResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<CreditQuoteResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<CreditQuoteResponse>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(Permission)]
    public async Task<IActionResult> Preview(
        [FromBody] OrganizationCreditPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _credits.PreviewForOrganizationAsync(request, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    /// <summary>
    /// Buys standalone credits: charges the organization's saved card once, or opens a hosted
    /// checkout when it has none, and grants the credits to the organization.
    /// </summary>
    /// <remarks>
    /// <c>idempotencyKey</c> is required. Sending the same key and the same request again returns
    /// the first result with <c>replayed</c> set, and never charges a second time; the same key
    /// with a different request is refused. The invoice is issued in the background once the
    /// payment settles, and appears under <c>organization-credits/invoices</c>.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreditPurchaseResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CreditPurchaseResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<CreditPurchaseResponse>), StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(typeof(ApiResponse<CreditPurchaseResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<CreditPurchaseResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<CreditPurchaseResponse>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(Permission)]
    public async Task<IActionResult> Purchase(
        [FromBody] OrganizationCreditPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _credits.PurchaseForOrganizationAsync(request, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    /// <summary>The organization's standalone grants, newest first, with their status as it is now.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CreditGrantResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(Permission)]
    public async Task<IActionResult> ListGrants(
        [FromQuery] string? organizationId,
        [FromQuery] string? meterKey,
        [FromQuery] CreditGrantStatus? status,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _credits.ListOrganizationGrantsAsync(organizationId, meterKey, status, correlationId, cancellationToken))
            .ToActionResult(correlationId);
    }

    /// <summary>Gives the organization standalone credits without a charge. The reason is required and kept.</summary>
    [HttpPost("grants")]
    [ProducesResponseType(typeof(ApiResponse<CreditGrantResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CreditGrantResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<CreditGrantResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(Permission)]
    public async Task<IActionResult> Grant(
        [FromBody] OrganizationGrantCreditsRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _credits.GrantToOrganizationAsync(request, correlationId, cancellationToken)).ToActionResult(correlationId);
    }

    [HttpGet("purchases")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CreditPurchaseResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(Permission)]
    public async Task<IActionResult> ListPurchases(
        [FromQuery] string? organizationId,
        [FromQuery] CreditPurchaseStatus? status,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return (await _credits.ListOrganizationPurchasesAsync(organizationId, status, correlationId, cancellationToken))
            .ToActionResult(correlationId);
    }

    /// <summary>The invoices for the organization's standalone credit purchases, newest first.</summary>
    [HttpGet("invoices")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionFinancialDocumentHistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionFinancialDocumentHistoryResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProtectedEndPoint(Permission)]
    public async Task<IActionResult> ListInvoices(
        [FromQuery] GetFinancialDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        request.SubscriptionId = null;
        request.StandaloneCreditsOnly = true;
        return (await _documents.ListAsync(request, correlationId, cancellationToken)).ToActionResult(correlationId);
    }
}
