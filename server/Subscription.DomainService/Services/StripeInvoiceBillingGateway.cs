using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Charges a subscription's renewal as a standalone Stripe Invoice: an item, an invoice, a
/// finalize, and a pay — all raised and settled within this one call, on Blocks' own schedule.
/// </summary>
/// <remarks>
/// No Stripe Subscription object exists behind this, so nothing here delegates dunning to
/// Stripe's own Smart Retries — that stays <see cref="SubscriptionRenewalService"/>'s job, same
/// as with <see cref="RecurringChargeBillingGateway"/>. What changes is that a successful charge
/// now produces a real Stripe Invoice document instead of a bare PaymentIntent.
/// <para>
/// Claiming and unprotecting the stored card is done directly against
/// <c>Payment.DomainService</c>'s repositories rather than through
/// <c>RecurringPaymentInitiationService</c>: that service is built around
/// <c>PaymentDetail</c>'s Authorized/Captured/Refused model, which an invoice's
/// draft/open/paid/uncollectible lifecycle does not map onto — forcing it to would teach the
/// payment module the word "Invoice."
/// </para>
/// </remarks>
public sealed class StripeInvoiceBillingGateway : ISubscriptionBillingGateway
{
    private readonly IPaymentProviderCache _providers;
    private readonly IPaymentRepository _payments;
    private readonly IStoredPaymentMethodRepository _storedMethods;
    private readonly IProviderTokenProtector _tokenProtector;
    private readonly IStripeInvoiceClient _invoices;
    private readonly ILogger<StripeInvoiceBillingGateway> _logger;
    private readonly TimeProvider _time;

    public StripeInvoiceBillingGateway(
        IPaymentProviderCache providers,
        IPaymentRepository payments,
        IStoredPaymentMethodRepository storedMethods,
        IProviderTokenProtector tokenProtector,
        IStripeInvoiceClient invoices,
        ILogger<StripeInvoiceBillingGateway> logger,
        TimeProvider? time = null)
    {
        _providers = providers;
        _payments = payments;
        _storedMethods = storedMethods;
        _tokenProtector = tokenProtector;
        _invoices = invoices;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<string>> ChargeAsync(
        SubscriptionChargeRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ProviderCustomerId))
        {
            _logger.LogWarning(
                "A Stripe renewal was attempted with no provider customer on record");

            return Unavailable("subscription_customer_unresolved", correlationId);
        }

        var provider = await _providers.GetAsync(
            request.TenantId,
            request.OrganizationId,
            request.ProviderName,
            () => _payments.GetProviderAsync(
                request.TenantId,
                request.OrganizationId,
                request.ProviderName,
                cancellationToken));

        if (provider is not { IsEnabled: true, ApiKey.Length: > 0 })
        {
            return Unavailable("subscription_provider_unavailable", correlationId);
        }

        var method = await _storedMethods.GetAsync(
            request.TenantId,
            request.StoredPaymentMethodId,
            cancellationToken);

        if (method is null)
        {
            return Unavailable("stored_payment_method_not_found", correlationId);
        }

        var leaseId = Guid.NewGuid().ToString("N");
        var claimed = await _storedMethods.TryClaimForPaymentAsync(
            request.TenantId,
            method.ItemId,
            method.ShopperReference ?? string.Empty,
            leaseId,
            _time.GetUtcNow().UtcDateTime.AddSeconds(60),
            cancellationToken);

        if (claimed is null)
        {
            return SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.Conflict,
                "stored_payment_method_in_use",
                "The stored payment method is changing or already in use.",
                correlationId);
        }

        try
        {
            return await ChargeClaimedMethodAsync(
                provider,
                claimed,
                request,
                idempotencyKey,
                correlationId,
                cancellationToken);
        }
        finally
        {
            await _storedMethods.ReleasePaymentClaimAsync(
                request.TenantId,
                claimed.ItemId,
                leaseId,
                CancellationToken.None);
        }
    }

    private async Task<SubscriptionOperationResult<string>> ChargeClaimedMethodAsync(
        PaymentProvider provider,
        StoredPaymentMethod claimed,
        SubscriptionChargeRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProtector.UnprotectAsync(claimed, cancellationToken);
        var paymentMethodId = token.ProviderToken;

        if (!token.IsRead)
        {
            return Unavailable("stored_payment_method_token_unavailable", correlationId);
        }

        // The invoice comes first so its line can name it. The reverse order leaves the line
        // pending for the next invoice to sweep up, and recent Stripe API versions default
        // pending_invoice_items_behavior to exclude — the invoice then finalizes at zero, reads
        // as paid because nothing is owed, and the renewal completes having collected nothing.
        var invoice = await _invoices.CreateInvoiceAsync(
            provider,
            request.ProviderCustomerId!,
            paymentMethodId,
            $"{idempotencyKey}:invoice",
            cancellationToken);

        if (!invoice.IsSuccess)
        {
            paymentMethodId = string.Empty;

            return Rejected(invoice, "subscription_invoice_create_failed", correlationId);
        }

        var item = await _invoices.CreateInvoiceItemAsync(
            provider,
            request.ProviderCustomerId!,
            invoice.InvoiceOrItemId!,
            request.AmountMinor,
            request.CurrencyCode,
            request.Description ?? "Subscription renewal",
            $"{idempotencyKey}:item",
            cancellationToken);

        if (!item.IsSuccess)
        {
            paymentMethodId = string.Empty;
            await _invoices.VoidInvoiceAsync(provider, invoice.InvoiceOrItemId!, cancellationToken);

            return Rejected(item, "subscription_invoice_item_failed", correlationId);
        }

        var finalized = await _invoices.FinalizeInvoiceAsync(
            provider,
            invoice.InvoiceOrItemId!,
            $"{idempotencyKey}:finalize",
            cancellationToken);

        if (!finalized.IsSuccess)
        {
            paymentMethodId = string.Empty;
            await _invoices.VoidInvoiceAsync(provider, invoice.InvoiceOrItemId!, cancellationToken);

            return Rejected(finalized, "subscription_invoice_finalize_failed", correlationId);
        }

        // A finalized invoice must owe exactly what this renewal asked for. Anything else means
        // the amount never reached Stripe as intended — a line left off leaves nothing owed, and
        // "nothing owed" arrives here indistinguishable from "already settled". Failing closed
        // keeps the subscription unpaid and visible instead of advancing a period for free.
        if (finalized.AmountMinor is { } owed && owed != request.AmountMinor)
        {
            paymentMethodId = string.Empty;
            await _invoices.VoidInvoiceAsync(provider, invoice.InvoiceOrItemId!, cancellationToken);

            _logger.LogError(
                "A Stripe renewal invoice was not for the amount charged; the renewal was " +
                "abandoned rather than credited InvoiceHash={InvoiceHash} " +
                "ExpectedMinor={ExpectedMinor} InvoicedMinor={InvoicedMinor}",
                PaymentLogValue.Hash(invoice.InvoiceOrItemId!),
                request.AmountMinor,
                owed);

            return Unavailable("subscription_invoice_amount_mismatch", correlationId);
        }

        if (IsPaid(finalized.Status))
        {
            paymentMethodId = string.Empty;

            // Finalizing a charge_automatically invoice collects it there and then; only
            // Stripe's own retry schedule is withheld by auto_advance. Paying again answers
            // "Invoice is already paid", which read as a decline and had this report a failed
            // renewal over money that had in fact moved — then try to void the invoice that
            // proved it. The period must advance on this, not on a second charge.
            _logger.LogInformation(
                "Subscription renewal was collected when its Stripe invoice was finalized " +
                "InvoiceHash={InvoiceHash}",
                PaymentLogValue.Hash(invoice.InvoiceOrItemId!));

            return SubscriptionOperationResult<string>.Success(
                invoice.InvoiceOrItemId!,
                correlationId);
        }

        var paid = await _invoices.PayInvoiceAsync(
            provider,
            invoice.InvoiceOrItemId!,
            paymentMethodId,
            $"{idempotencyKey}:pay",
            cancellationToken);
        paymentMethodId = string.Empty;

        if (!paid.IsSuccess)
        {
            // A pay call that failed over an invoice already paid took the money all the same —
            // Stripe collected it between finalizing and here. Voiding would be an attempt to
            // cancel a settled invoice, and reporting a decline would bill the customer twice on
            // the next attempt.
            if (IsPaid(paid.Status))
            {
                return SubscriptionOperationResult<string>.Success(
                    invoice.InvoiceOrItemId!,
                    correlationId);
            }

            await _invoices.VoidInvoiceAsync(provider, invoice.InvoiceOrItemId!, cancellationToken);

            return Rejected(paid, "subscription_invoice_payment_declined", correlationId);
        }

        _logger.LogInformation(
            "Subscription renewal charged through a Stripe invoice InvoiceHash={InvoiceHash}",
            PaymentLogValue.Hash(invoice.InvoiceOrItemId!));

        return SubscriptionOperationResult<string>.Success(invoice.InvoiceOrItemId!, correlationId);
    }

    /// <summary>
    /// Stripe's terminal settled status. Compared as an ordinal so a status this code does not
    /// know reads as unpaid rather than as money received.
    /// </summary>
    private static bool IsPaid(string? status) =>
        string.Equals(status, "paid", StringComparison.Ordinal);

    private static SubscriptionOperationResult<string> Rejected(
        StripeInvoiceCallResult result,
        string fallbackCode,
        string correlationId) =>
        SubscriptionOperationResult<string>.Failure(
            result.Outcome switch
            {
                StripeInvoiceOutcome.Unavailable => PaymentFailureKind.Unavailable,
                StripeInvoiceOutcome.Timeout => PaymentFailureKind.Timeout,
                _ => PaymentFailureKind.ProviderRejected
            },
            result.SafeErrorCode ?? fallbackCode,
            "The payment provider declined this charge.",
            correlationId);

    private static SubscriptionOperationResult<string> Unavailable(
        string errorCode,
        string correlationId) =>
        SubscriptionOperationResult<string>.Failure(
            PaymentFailureKind.Unavailable,
            errorCode,
            "This subscription cannot be charged right now.",
            correlationId);
}
