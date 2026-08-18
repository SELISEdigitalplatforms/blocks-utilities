using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Hands a subscriber the invoice document behind one of their subscription payments.
/// </summary>
/// <remarks>
/// The provider's own download link is never returned. It carries no authentication and does not
/// expire, so returning one would grant permanent access to anyone it reached and put the document
/// beyond this service's reach the moment it left. Fetching it here keeps the check on every
/// download, and keeps access revocable by revoking the caller's.
/// <para>
/// The link is also read fresh each time rather than stored, so nothing in the database is a
/// standing key to a customer's billing document.
/// </para>
/// </remarks>
public sealed class SubscriptionInvoiceDocumentService : ISubscriptionInvoiceDocumentService
{
    private readonly ISubscriptionContextResolver _context;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderCache _providers;
    private readonly IStripeInvoiceClient _invoices;
    private readonly ILogger<SubscriptionInvoiceDocumentService> _logger;

    public SubscriptionInvoiceDocumentService(
        ISubscriptionContextResolver context,
        IPaymentRepository payments,
        IPaymentProviderCache providers,
        IStripeInvoiceClient invoices,
        ILogger<SubscriptionInvoiceDocumentService> logger)
    {
        _context = context;
        _payments = payments;
        _providers = providers;
        _invoices = invoices;
        _logger = logger;
    }

    public async Task<SubscriptionOperationResult<SubscriptionInvoiceDocument>> GetAsync(
        string paymentId,
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

        var payment = await _payments.GetByIdAsync(
            context.TenantId,
            paymentId,
            cancellationToken);

        // One answer for absent, for another organization's, and for a payment that is not a
        // subscription invoice at all. Distinguishing them would let a caller enumerate the
        // tenant's payments by the shape of the refusal.
        if (payment is null ||
            !string.Equals(
                payment.PaymentFlow,
                PaymentFlows.SubscriptionInvoice,
                StringComparison.Ordinal) ||
            !OwnedBy(payment.CustomerOrganizationId, context.OrganizationId) ||
            payment.ProviderInvoiceId is not { Length: > 0 } invoiceId)
        {
            return NotFound(correlationId);
        }

        // Resolved under the payment's own organization, which is the merchant scope that took
        // the money — not the subscriber's, which has no provider configured.
        var provider = await _providers.GetAsync(
            context.TenantId,
            payment.OrganizationId ?? context.OrganizationId,
            payment.ProviderName,
            () => _payments.GetProviderAsync(
                context.TenantId,
                payment.OrganizationId ?? context.OrganizationId,
                payment.ProviderName,
                cancellationToken));

        if (provider is not { IsEnabled: true, ApiKey.Length: > 0 })
        {
            _logger.LogWarning(
                "An invoice document was asked for but its provider could not be resolved " +
                "Provider={Provider}",
                PaymentLogValue.Label(payment.ProviderName));

            return SubscriptionOperationResult<SubscriptionInvoiceDocument>.Failure(
                PaymentFailureKind.Unavailable,
                "subscription_invoice_provider_unavailable",
                "This invoice cannot be fetched right now.",
                correlationId);
        }

        var document = await _invoices.DownloadInvoicePdfAsync(
            provider,
            invoiceId,
            cancellationToken);

        if (document is null)
        {
            return SubscriptionOperationResult<SubscriptionInvoiceDocument>.Failure(
                PaymentFailureKind.Unavailable,
                "subscription_invoice_document_unavailable",
                "This invoice's document could not be fetched right now.",
                correlationId);
        }

        return SubscriptionOperationResult<SubscriptionInvoiceDocument>.Success(
            new SubscriptionInvoiceDocument(
                document.Content,
                document.ContentType,
                FileNameFor(document.InvoiceNumber, payment.ItemId)),
            correlationId);
    }

    /// <summary>
    /// Whether this payment belongs to the asking organization.
    /// </summary>
    /// <remarks>
    /// Payments recorded before the subscriber was captured carry no owning organization. Those
    /// are refused rather than shown to whoever asks: an unattributed billing document is exactly
    /// the thing not to hand out on a guess.
    /// </remarks>
    private static bool OwnedBy(string? subscriberOrganizationId, string callerOrganizationId) =>
        subscriberOrganizationId is { Length: > 0 } &&
        string.Equals(subscriberOrganizationId, callerOrganizationId, StringComparison.Ordinal);

    private static string FileNameFor(string? invoiceNumber, string paymentId) =>
        $"invoice-{Sanitize(invoiceNumber ?? paymentId)}.pdf";

    /// <summary>
    /// Keeps a provider-supplied invoice number from shaping the download filename, which is
    /// echoed into a Content-Disposition header.
    /// </summary>
    private static string Sanitize(string value)
    {
        var safe = new string([.. value
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')]);

        return safe.Length == 0 ? "document" : safe[..Math.Min(safe.Length, 60)];
    }

    private static SubscriptionOperationResult<SubscriptionInvoiceDocument> NotFound(
        string correlationId) =>
        SubscriptionOperationResult<SubscriptionInvoiceDocument>.Failure(
            PaymentFailureKind.NotFound,
            "subscription_invoice_not_found",
            "No invoice was found for this payment.",
            correlationId);
}
