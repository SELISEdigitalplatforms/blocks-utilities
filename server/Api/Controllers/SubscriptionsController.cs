using Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;

namespace Api.Controllers;

/// <summary>
/// An organization's own subscription. Served under <c>/api/subscriptions</c>.
/// </summary>
/// <remarks>
/// Every endpoint resolves its organization from the authenticated caller. A caller may also
/// name one explicitly, but it is honored only for the platform console — see the module
/// README's "Console organization override" section — so an identifier here is not simply
/// something anyone can change.
/// </remarks>
[ApiController]
[Authorize]
[Route("subscriptions")]
public sealed class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionCheckoutService _checkout;
    private readonly ISubscriptionCancellationService _cancellation;
    private readonly ISubscriptionPlanChangeService _planChange;
    private readonly ISubscriptionInvoiceDocumentService _invoiceDocuments;
    private readonly ISubscriptionQuantityChangeService _quantityChange;
    private readonly ISubscriptionFinancialDocumentHistoryService _documents;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionAuditTrail _audit;
    private readonly ISubscriptionAuditRepository _auditRepository;

    public SubscriptionsController(
        ISubscriptionCheckoutService checkout,
        ISubscriptionCancellationService cancellation,
        ISubscriptionPlanChangeService planChange,
        ISubscriptionInvoiceDocumentService invoiceDocuments,
        ISubscriptionFinancialDocumentHistoryService documents,
        ISubscriptionQuantityChangeService quantityChange,
        ISubscriptionContextResolver contextResolver,
        ISubscriptionAuditTrail audit,
        ISubscriptionAuditRepository auditRepository)
    {
        _checkout = checkout;
        _cancellation = cancellation;
        _planChange = planChange;
        _invoiceDocuments = invoiceDocuments;
        _documents = documents;
        _quantityChange = quantityChange;
        _contextResolver = contextResolver;
        _audit = audit;
        _auditRepository = auditRepository;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Subscribe(
        [FromBody] CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _checkout.SubscribeAsync(
            request,
            correlationId,
            cancellationToken);

        await AuditAsync("Subscribe", request.OrganizationId, result.Value?.SubscriptionId,
            result.IsSuccess, result.ErrorCode, result.FailureKind.ToString(), correlationId,
            result.Value?.RecurringAmountMinor, result.Value?.CurrencyCode, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// The caller's own subscription.
    /// </summary>
    /// <remarks>
    /// Immediately after paying this may still report <c>Incomplete</c>: the shopper's browser
    /// usually returns before the provider's webhook lands, and only the webhook is treated as
    /// proof that money moved. Clients should expect a short pending state rather than assume
    /// something failed.
    /// </remarks>
    [HttpGet("current")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrent(
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _checkout.GetCurrentAsync(
            organizationId,
            correlationId,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Cancels a subscription.
    /// </summary>
    /// <remarks>
    /// By default the subscription keeps granting until the period already paid for runs out,
    /// and simply stops renewing. Pass <c>immediately</c> only where the customer is entitled
    /// to stop at once. An incomplete checkout has no paid period, so it always ends immediately
    /// and releases the organization to subscribe again.
    /// </remarks>
    [HttpDelete("{subscriptionId}")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Cancel(
        string subscriptionId,
        [FromQuery] bool immediately,
        [FromQuery] string? reason,
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _cancellation.CancelAsync(
            subscriptionId,
            immediately,
            reason,
            organizationId,
            correlationId,
            cancellationToken);

        await AuditAsync("Cancel", organizationId, subscriptionId, result.IsSuccess,
            result.ErrorCode, result.FailureKind.ToString(), correlationId, null, null,
            cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Moves the subscription to a different price, mid-period.
    /// </summary>
    /// <remarks>
    /// An upgrade is charged immediately for the prorated difference; a downgrade is credited
    /// toward future renewals rather than refunded. A trial has paid nothing yet, so its plan
    /// simply swaps with no charge or credit either way.
    /// </remarks>
    [HttpPut("{subscriptionId}/plan")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePlan(
        string subscriptionId,
        [FromBody] ChangeSubscriptionPlanRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _planChange.ChangePlanAsync(
            subscriptionId,
            request,
            correlationId,
            cancellationToken);

        await AuditAsync("ChangePlan", request.OrganizationId, subscriptionId, result.IsSuccess,
            result.ErrorCode, result.FailureKind.ToString(), correlationId,
            result.Value?.RecurringAmountMinor, result.Value?.CurrencyCode, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// What changing the purchased quantity would cost, and when it would take effect.
    /// </summary>
    /// <remarks>
    /// Calculates and validates exactly as the update does, and mutates nothing. Call it before
    /// asking an administrator to confirm: an increase is payable now, a decrease takes effect at
    /// the end of the period already paid for, and the two need different wording.
    /// </remarks>
    [HttpPost("{subscriptionId}/quantities/preview")]
    [ProducesResponseType(typeof(ApiResponse<QuantityChangeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<QuantityChangeResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<QuantityChangeResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<QuantityChangeResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PreviewQuantityChange(
        string subscriptionId,
        [FromBody] ChangeQuantityRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _quantityChange.PreviewAsync(
            subscriptionId,
            request,
            correlationId,
            cancellationToken);

        await AuditAsync("PreviewQuantityChange", request.OrganizationId, subscriptionId,
            result.IsSuccess, result.ErrorCode, result.FailureKind.ToString(), correlationId,
            result.Value?.ProratedChargeMinor, result.Value?.CurrencyCode, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Changes how many units the subscription has bought, without changing its plan.
    /// </summary>
    /// <remarks>
    /// An increase hands over the units at once, so it is charged at once — the prorated
    /// difference for the rest of the period, taken before the quantity moves. A declined card
    /// leaves the subscription exactly as it was.
    /// <para>
    /// A decrease is not refunded, so it is scheduled rather than applied: the units stay
    /// available until the period ends and the renewal bills the smaller quantity.
    /// </para>
    /// <para>
    /// <c>version</c> is required and applied as a compare-and-set, so a stale administrator
    /// cannot overwrite a seat count somebody else has already changed.
    /// </para>
    /// </remarks>
    [HttpPut("{subscriptionId}/quantities")]
    [ProducesResponseType(typeof(ApiResponse<QuantityChangeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<QuantityChangeResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<QuantityChangeResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<QuantityChangeResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<QuantityChangeResponse>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangeQuantity(
        string subscriptionId,
        [FromBody] ChangeQuantityRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _quantityChange.ChangeAsync(
            subscriptionId,
            request,
            correlationId,
            cancellationToken);

        await AuditAsync("ChangeQuantity", request.OrganizationId, subscriptionId,
            result.IsSuccess, result.ErrorCode, result.FailureKind.ToString(), correlationId,
            result.Value?.ProratedChargeMinor, result.Value?.CurrencyCode, cancellationToken,
            result.Value?.ChargePaymentDetailId);

        return result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Withdraws a scheduled decrease, leaving the current quantity in place.
    /// </summary>
    [HttpDelete("{subscriptionId}/quantities/pending")]
    [ProducesResponseType(typeof(ApiResponse<QuantityChangeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<QuantityChangeResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<QuantityChangeResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CancelPendingQuantityChange(
        string subscriptionId,
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _quantityChange.CancelPendingAsync(
            subscriptionId,
            organizationId,
            correlationId,
            cancellationToken);

        await AuditAsync("CancelPendingQuantityChange", organizationId, subscriptionId,
            result.IsSuccess, result.ErrorCode, result.FailureKind.ToString(), correlationId,
            null, null, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    /// <summary>Returns the immutable lifecycle trail used to investigate this subscription.</summary>
    /// <remarks>
    /// Results are tenant- and organization-scoped. They intentionally omit actor identifiers,
    /// payment identifiers and all provider/customer secrets.
    /// </remarks>
    [HttpGet("{subscriptionId}/audit")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubscriptionAuditEventResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAuditTrail(
        string subscriptionId,
        [FromQuery] string? organizationId,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var resolution = await _contextResolver.ResolveAsync(
            correlationId, organizationId, cancellationToken);
        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return resolution.ToFailure<IReadOnlyList<SubscriptionAuditEventResponse>>(correlationId)
                .ToActionResult(correlationId);
        }

        var context = resolution.Context;
        var events = await _auditRepository.ListAsync(
            context.TenantId, context.OrganizationId, subscriptionId,
            limit <= 0 ? 100 : limit, cancellationToken);

        var response = events.Select(x => new SubscriptionAuditEventResponse
        {
            EventId = x.ItemId,
            OperationId = x.OperationId,
            CorrelationId = x.CorrelationId,
            Operation = x.Operation,
            Stage = x.Stage,
            Outcome = x.Outcome,
            Source = x.Source,
            AmountMinor = x.AmountMinor,
            CurrencyCode = x.CurrencyCode,
            FromStatus = x.FromStatus,
            ToStatus = x.ToStatus,
            ErrorCode = x.ErrorCode,
            FailureKind = x.FailureKind,
            Attempt = x.Attempt,
            OccurredAtUtc = x.OccurredAtUtc
        }).ToList();

        return SubscriptionOperationResult<IReadOnlyList<SubscriptionAuditEventResponse>>
            .Success(response, correlationId).ToActionResult(correlationId);
    }

    private async Task AuditAsync(
        string operation,
        string? requestedOrganizationId,
        string? subscriptionId,
        bool success,
        string? errorCode,
        string failureKind,
        string correlationId,
        long? amountMinor,
        string? currencyCode,
        CancellationToken cancellationToken,
        string? paymentDetailId = null)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId, requestedOrganizationId, cancellationToken);
        if (!resolution.IsSuccess || resolution.Context is null) return;

        var context = resolution.Context;
        await _audit.RecordAsync(new SubscriptionAuditEvent
        {
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            SubscriptionId = subscriptionId,
            OperationId = correlationId,
            CorrelationId = correlationId,
            Operation = operation,
            Stage = "Completed",
            Outcome = success ? "Succeeded" : "Rejected",
            Source = "Api",
            ActorId = context.ActorId,
            UserId = context.UserId,
            PaymentDetailId = paymentDetailId,
            AmountMinor = amountMinor,
            CurrencyCode = currencyCode,
            ErrorCode = errorCode,
            FailureKind = success ? null : failureKind
        }, cancellationToken);
    }

    /// <summary>
    /// Downloads one financial document as a PDF.
    /// </summary>
    /// <remarks>
    /// <paramref name="documentId"/> is the <c>documentId</c> reported by the invoice list. A payment
    /// id is also accepted, so links handed out by the previous payment-derived history keep working:
    /// the application's own document for that payment is served where one exists, and only a payment
    /// from before the ledger existed falls through to the provider's stored copy. That fallback is
    /// deprecated and will be removed once no pre-migration payments remain.
    /// <para>
    /// The bytes are served from here rather than as a link to storage or to the provider. Either kind
    /// of URL is a bearer token for the document, and one that has left the building cannot be revoked
    /// by revoking this caller's access — which is the only lever there is.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Sends a document's mail once more. Platform console only.
    /// </summary>
    /// <remarks>
    /// The counterpart of sending at most once. Nothing automatic re-sends a mail whose outcome could
    /// not be established — a broker can accept a message and lose the acknowledgement on the way back,
    /// so a failed publish is not evidence of non-delivery, and retrying it would risk a second invoice
    /// arriving at somebody's finance mailbox. That leaves a person to decide, and this is the decision.
    /// <para>
    /// Whoever calls this is accepting that the subscriber may receive the same invoice twice. Returns
    /// as soon as the resend is durable; the mail goes out on the same work type as every other
    /// document's, so a resend cannot behave differently from a first attempt.
    /// </para>
    /// </remarks>
    [HttpPost("invoices/{documentId}/resend")]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionFinancialDocumentResendResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResendInvoice(
        string documentId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _documents.ResendAsync(documentId, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }

    [HttpGet("invoices/{documentId}/pdf")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetInvoicePdf(
        string documentId,
        [FromQuery] string? organizationId,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;

        var result = await _documents.GetPdfAsync(
            documentId,
            organizationId,
            correlationId,
            cancellationToken);

        if (result.IsSuccess && result.Value is { } document)
        {
            return File(document.Content, document.ContentType, document.FileName);
        }

        // Only "no such document" is worth a second look. A pending render or an unreachable store
        // has already found the right document, and asking the provider about it would answer a
        // different question.
        if (result.FailureKind != PaymentFailureKind.NotFound)
        {
            return result.ToActionResult(correlationId);
        }

        var legacy = await _invoiceDocuments.GetAsync(
            documentId,
            organizationId,
            correlationId,
            cancellationToken);

        return legacy.IsSuccess && legacy.Value is { } provider
            ? File(provider.Content, provider.ContentType, provider.FileName)
            : result.ToActionResult(correlationId);
    }

    /// <summary>
    /// Lists the calling organization's invoices, trial invoices and credit notes, newest first.
    /// </summary>
    /// <remarks>
    /// Answered from the application's own document ledger rather than from payments. Every settled
    /// charge, every trial start and every confirmed refund has a document here, which is why a trial
    /// and a credit note can appear at all — a payment-derived history could only describe things that
    /// had a payment.
    /// </remarks>
    [HttpGet("invoices")]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionFinancialDocumentHistoryResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<SubscriptionFinancialDocumentHistoryResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetInvoiceHistory(
        [FromQuery] GetFinancialDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _documents.ListAsync(request, correlationId, cancellationToken);

        return result.ToActionResult(correlationId);
    }
}
