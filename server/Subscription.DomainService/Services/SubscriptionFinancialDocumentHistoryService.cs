using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

public sealed class SubscriptionFinancialDocumentHistoryService :
    ISubscriptionFinancialDocumentHistoryService
{
    private const int MaximumPageSize = 100;

    private readonly ISubscriptionContextResolver _context;
    private readonly ISubscriptionFinancialDocumentRepository _documents;
    private readonly IFinancialDocumentFileStore _files;

    public SubscriptionFinancialDocumentHistoryService(
        ISubscriptionContextResolver context,
        ISubscriptionFinancialDocumentRepository documents,
        IFinancialDocumentFileStore files)
    {
        _context = context;
        _documents = documents;
        _files = files;
    }

    public async Task<SubscriptionOperationResult<SubscriptionFinancialDocumentHistoryResponse>>
        ListAsync(
            GetFinancialDocumentsRequest request,
            string correlationId,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.PageSize is < 1 or > MaximumPageSize)
        {
            return Invalid(
                correlationId,
                nameof(request.PageSize),
                $"PageSize must be between 1 and {MaximumPageSize}.");
        }

        if (request.IssuedFromUtc is { } from &&
            request.IssuedToUtc is { } to &&
            from > to)
        {
            return Invalid(
                correlationId,
                nameof(request.IssuedFromUtc),
                "IssuedFromUtc must not be later than IssuedToUtc.");
        }

        var resolution = await _context.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<SubscriptionFinancialDocumentHistoryResponse>(
                correlationId);
        }

        FinancialDocumentCursor? after = null;
        if (request.After is not null &&
            !FinancialDocumentCursorCodec.TryDecode(
                request.After,
                context.OrganizationId,
                out after))
        {
            return Invalid(correlationId, nameof(request.After), "After is not a valid cursor.");
        }

        var page = await _documents.ListAsync(
            context.TenantId,
            context.OrganizationId,
            request.SubscriptionId,
            request.DocumentType,
            request.Status,
            request.IssuedFromUtc?.ToUniversalTime(),
            request.IssuedToUtc?.ToUniversalTime(),
            request.PageSize,
            after,
            cancellationToken);

        var last = page.Items.LastOrDefault();

        return SubscriptionOperationResult<SubscriptionFinancialDocumentHistoryResponse>.Success(
            new SubscriptionFinancialDocumentHistoryResponse
            {
                Items = [.. page.Items.Select(
                    document => Map(document, request.OrganizationId))],
                PageInfo = new SubscriptionFinancialDocumentPageInfoResponse
                {
                    PageSize = request.PageSize,
                    HasNextPage = page.HasMore,
                    NextCursor = page.HasMore && last is not null
                        ? FinancialDocumentCursorCodec.Encode(
                            context.OrganizationId,
                            new FinancialDocumentCursor(last.IssuedAtUtc, last.ItemId))
                        : null
                }
            },
            correlationId);
    }

    public async Task<SubscriptionOperationResult<SubscriptionInvoiceDocument>> GetPdfAsync(
        string documentId,
        string? requestedOrganizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(
            correlationId,
            requestedOrganizationId,
            cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<SubscriptionInvoiceDocument>(correlationId);
        }

        // Tried as a document id first and as a payment id second, so the route the old
        // payment-derived history handed out keeps working against the new ledger. A client holding
        // a bookmarked payment id gets the application's own invoice rather than a 404, and only a
        // payment with no document at all falls through to the provider's copy.
        var document =
            await _documents.GetAsync(context.TenantId, documentId, cancellationToken)
            ?? await _documents.FindBySourceKeyAsync(
                context.TenantId,
                FinancialDocumentSourceKey.ForPayment(documentId),
                cancellationToken);

        // One answer for absent and for another organization's. Distinguishing them would let a
        // caller enumerate the tenant's documents by the shape of the refusal.
        if (document is null ||
            !string.Equals(
                document.OrganizationId,
                context.OrganizationId,
                StringComparison.Ordinal))
        {
            return NotFound(correlationId);
        }

        if (document.Delivery.StorageId is not { Length: > 0 } storageId)
        {
            // Issued but not yet rendered. A distinct code from "not found", because the answer to
            // this one is to try again shortly rather than to conclude the document does not exist.
            return SubscriptionOperationResult<SubscriptionInvoiceDocument>.Failure(
                PaymentFailureKind.Unavailable,
                "subscription_document_pdf_pending",
                "This document has been issued but its PDF is not ready yet.",
                correlationId);
        }

        var content = await _files.ReadAsync(storageId, cancellationToken);
        if (content is not { Length: > 0 })
        {
            return SubscriptionOperationResult<SubscriptionInvoiceDocument>.Failure(
                PaymentFailureKind.Unavailable,
                "subscription_document_pdf_unavailable",
                "This document's PDF could not be fetched right now.",
                correlationId);
        }

        return SubscriptionOperationResult<SubscriptionInvoiceDocument>.Success(
            new SubscriptionInvoiceDocument(
                content,
                "application/pdf",
                SubscriptionFinancialDocumentDeliveryService.FileNameFor(document)),
            correlationId);
    }

    private static SubscriptionFinancialDocumentResponse Map(
        SubscriptionFinancialDocument document,
        string? requestedOrganizationId)
    {
        var downloadUrl =
            $"/api/subscriptions/invoices/{Uri.EscapeDataString(document.ItemId)}/pdf";

        if (!string.IsNullOrWhiteSpace(requestedOrganizationId))
        {
            // Echoed back so a console client can follow the link without re-deciding whose
            // documents it was looking at.
            downloadUrl += $"?organizationId={Uri.EscapeDataString(requestedOrganizationId)}";
        }

        return new SubscriptionFinancialDocumentResponse
        {
            DocumentId = document.ItemId,
            DocumentNumber = document.DocumentNumber,
            DocumentType = document.DocumentType.ToString(),
            Status = document.Status.ToString(),
            IssuedAtUtc = document.IssuedAtUtc,
            SubscriptionId = document.SubscriptionId,
            CurrencyCode = document.CurrencyCode,
            PlanCode = document.Subject.PlanCode,
            PlanName = document.Subject.PlanName,
            PeriodStartUtc = Nullable(document.Period.StartUtc),
            PeriodEndUtc = Nullable(document.Period.EndUtc),
            PeriodLocalStart = Blank(document.Period.LocalStart),
            PeriodLocalEnd = Blank(document.Period.LocalEnd),
            TimeZoneId = document.Period.TimeZoneId,
            Amounts = new FinancialDocumentAmountsResponse
            {
                GrossSubtotalMinor = document.Amounts.GrossSubtotalMinor,
                AutomaticDiscountMinor = document.Amounts.AutomaticDiscountMinor,
                QuantityDiscountMinor = document.Amounts.QuantityDiscountMinor,
                PromotionalDiscountMinor = document.Amounts.PromotionalDiscountMinor,
                NetSubtotalMinor = document.Amounts.NetSubtotalMinor,
                TaxRateBasisPoints = document.Amounts.TaxRateBasisPoints,
                TaxMode = document.Amounts.TaxMode,
                TaxAmountMinor = document.Amounts.TaxAmountMinor,
                CreditAppliedMinor = document.Amounts.CreditAppliedMinor,
                TotalMinor = document.Amounts.TotalMinor,
                AutomaticDiscountBasisPoints = document.Amounts.AutomaticDiscountBasisPoints,
                QuantityDiscountBasisPoints = document.Amounts.QuantityDiscountBasisPoints,
                DiscountCombination = document.Amounts.DiscountCombination,
                PromotionCode = document.Amounts.PromotionCode
            },
            Settlement = document.Settlement is { } settlement
                ? new FinancialDocumentSettlementResponse
                {
                    Outgoing = Side(settlement.Outgoing),
                    Target = Side(settlement.Target),
                    CreditConsumedMinor = settlement.CreditConsumedMinor,
                    NetSettlementMinor = settlement.NetSettlementMinor
                }
                : null,
            Lines = [.. document.Lines.Select(line => new FinancialDocumentLineResponse
            {
                Description = line.Description,
                Quantity = line.Quantity,
                UnitAmountMinor = line.UnitAmountMinor,
                AmountMinor = line.AmountMinor,
                ItemKey = line.ItemKey
            })],
            Trial = document.Trial is { } trial
                ? new FinancialDocumentTrialResponse
                {
                    StartsAtUtc = trial.StartsAtUtc,
                    EndsAtUtc = trial.EndsAtUtc,
                    RequiresPaymentMethod = trial.RequiresPaymentMethod,
                    FirstBillingAtUtc = trial.FirstBillingAtUtc
                }
                : null,
            SubscriberLegalName = document.Subscriber.LegalName,
            BillingContactName = document.BillingContact.Name,
            BillingContactEmail = document.BillingContact.Email,
            InitiatedByName = document.InitiatedBy.Name,
            InitiatedByUserId = document.InitiatedBy.UserId,
            PaymentDetailId = document.PaymentDetailId,
            RefundId = document.RefundId,
            OriginalDocumentId = document.OriginalDocumentId,
            OriginalDocumentNumber = document.OriginalDocumentNumber,
            IsPdfAvailable = document.Delivery.StorageId is { Length: > 0 },
            PdfContentHash = document.Delivery.ContentHash,
            DownloadUrl = downloadUrl
        };
    }

    private static FinancialDocumentSettlementSideResponse Side(
        Payment.DomainService.Entities.SubscriptionSettlementSide side) =>
        new()
        {
            GrossAmountMinor = side.GrossAmountMinor,
            BuiltInDiscountMinor = side.BuiltInDiscountMinor,
            PromotionalDiscountMinor = side.PromotionalDiscountMinor,
            TaxAmountMinor = side.TaxAmountMinor,
            PeriodTotalMinor = side.PeriodTotalMinor,
            ProratedValueMinor = side.ProratedValueMinor
        };

    private static DateTime? Nullable(DateTime value) => value == default ? null : value;

    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static SubscriptionOperationResult<SubscriptionInvoiceDocument> NotFound(
        string correlationId) =>
        SubscriptionOperationResult<SubscriptionInvoiceDocument>.Failure(
            PaymentFailureKind.NotFound,
            "subscription_document_not_found",
            "No document was found.",
            correlationId);

    private static SubscriptionOperationResult<SubscriptionFinancialDocumentHistoryResponse> Invalid(
        string correlationId,
        string field,
        string message) =>
        SubscriptionOperationResult<SubscriptionFinancialDocumentHistoryResponse>.Failure(
            PaymentFailureKind.Validation,
            "subscription_document_query_invalid",
            "The document query is invalid.",
            correlationId,
            new Dictionary<string, string[]> { [field] = [message] });
}
