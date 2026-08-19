using Payment.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

public sealed class SubscriptionInvoiceHistoryService :
    ISubscriptionInvoiceHistoryService
{
    private const int MaximumPageSize = 100;

    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionInvoiceHistoryRepository _invoices;

    public SubscriptionInvoiceHistoryService(
        ISubscriptionContextResolver contextResolver,
        ISubscriptionInvoiceHistoryRepository invoices)
    {
        _contextResolver = contextResolver;
        _invoices = invoices;
    }

    public async Task<SubscriptionOperationResult<SubscriptionInvoiceHistoryResponse>> ListAsync(
        GetSubscriptionInvoicesRequest request,
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

        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);
        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<SubscriptionInvoiceHistoryResponse>(correlationId);
        }

        var context = resolution.Context!;
        SubscriptionInvoiceHistoryCursor? after = null;
        if (request.After is not null &&
            !SubscriptionInvoiceHistoryCursorCodec.TryDecode(
                request.After,
                context.OrganizationId,
                out after))
        {
            return Invalid(correlationId, nameof(request.After), "After is not a valid cursor.");
        }

        var page = await _invoices.ListAsync(
            context.TenantId,
            context.OrganizationId,
            request.PageSize,
            after,
            cancellationToken);

        var items = page.Items
            .Select(invoice => Map(invoice, request.OrganizationId))
            .ToArray();
        var last = page.Items.LastOrDefault();

        return SubscriptionOperationResult<SubscriptionInvoiceHistoryResponse>.Success(
            new SubscriptionInvoiceHistoryResponse
            {
                Items = items,
                PageInfo = new SubscriptionInvoiceHistoryPageInfoResponse
                {
                    PageSize = request.PageSize,
                    HasNextPage = page.HasMore,
                    NextCursor = page.HasMore && last is not null
                        ? SubscriptionInvoiceHistoryCursorCodec.Encode(
                            context.OrganizationId,
                            last)
                        : null
                }
            },
            correlationId);
    }

    private static SubscriptionInvoiceHistoryItemResponse Map(
        SubscriptionInvoiceHistoryRecord invoice,
        string? requestedOrganizationId)
    {
        var (subscriptionId, invoiceType, periodKey) = ParseOrderId(invoice.OrderId);
        var downloadUrl =
            $"/api/subscriptions/invoices/{Uri.EscapeDataString(invoice.PaymentDetailId)}/pdf";
        if (!string.IsNullOrWhiteSpace(requestedOrganizationId))
        {
            downloadUrl +=
                $"?organizationId={Uri.EscapeDataString(requestedOrganizationId)}";
        }

        return new SubscriptionInvoiceHistoryItemResponse
        {
            PaymentDetailId = invoice.PaymentDetailId,
            SubscriptionId = subscriptionId,
            InvoiceType = invoiceType,
            PeriodKey = periodKey,
            ProviderName = invoice.ProviderName,
            Description = invoice.Description ?? string.Empty,
            Amount = invoice.Amount,
            RefundedAmount = invoice.RefundedAmount,
            CurrencyCode = invoice.CurrencyCode,
            Status = invoice.Status,
            IssuedAtUtc = invoice.IssuedAtUtc.ToUniversalTime(),
            DownloadUrl = downloadUrl
        };
    }

    private static (string? SubscriptionId, string InvoiceType, string? PeriodKey) ParseOrderId(
        string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId) ||
            !orderId.StartsWith(SubscriptionConstants.OrderIdPrefix, StringComparison.Ordinal))
        {
            return (null, "Unknown", null);
        }

        var value = orderId[SubscriptionConstants.OrderIdPrefix.Length..];
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator is <= 0 || separator == value.Length - 1)
        {
            return (null, "Unknown", null);
        }

        var subscriptionId = value[..separator];
        var suffix = value[(separator + 1)..];
        if (suffix.StartsWith("planchange:", StringComparison.Ordinal))
        {
            return (subscriptionId, "PlanChange", null);
        }

        if (suffix.StartsWith("usage:", StringComparison.Ordinal))
        {
            var periodKey = suffix["usage:".Length..];
            return string.IsNullOrWhiteSpace(periodKey)
                ? (subscriptionId, "Usage", null)
                : (subscriptionId, "Usage", periodKey);
        }

        return (subscriptionId, "Renewal", suffix);
    }

    private static SubscriptionOperationResult<SubscriptionInvoiceHistoryResponse> Invalid(
        string correlationId,
        string field,
        string message) =>
        SubscriptionOperationResult<SubscriptionInvoiceHistoryResponse>.Failure(
            PaymentFailureKind.Validation,
            "subscription_invoice_query_invalid",
            "The invoice query is invalid.",
            correlationId,
            new Dictionary<string, string[]> { [field] = [message] });
}
