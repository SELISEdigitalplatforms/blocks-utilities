using System.Globalization;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Enums;

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
    private readonly ICurrencyMinorUnitResolver _amounts;
    private readonly ILogger<StripeInvoiceBillingGateway> _logger;
    private readonly TimeProvider _time;

    public StripeInvoiceBillingGateway(
        IPaymentProviderCache providers,
        IPaymentRepository payments,
        IStoredPaymentMethodRepository storedMethods,
        IProviderTokenProtector tokenProtector,
        IStripeInvoiceClient invoices,
        ICurrencyMinorUnitResolver amounts,
        ILogger<StripeInvoiceBillingGateway> logger,
        TimeProvider? time = null)
    {
        _providers = providers;
        _payments = payments;
        _storedMethods = storedMethods;
        _tokenProtector = tokenProtector;
        _invoices = invoices;
        _amounts = amounts;
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
        //
        // Which is why the currency has to be passed: creating the invoice first means Stripe
        // has no line to infer it from, so it guesses from the customer's history or the
        // merchant's default, and the line item is then refused for disagreeing with the guess.
        var invoice = await _invoices.CreateInvoiceAsync(
            provider,
            request.ProviderCustomerId!,
            paymentMethodId,
            request.CurrencyCode,
            $"{idempotencyKey}:invoice",
            cancellationToken);

        if (!invoice.IsSuccess)
        {
            paymentMethodId = string.Empty;

            return Rejected(invoice, "subscription_invoice_create_failed", correlationId);
        }

        // A downloadable invoice must explain the charge without recalculating it: subtotal plus
        // tax, less any banked subscription credit. Use that breakdown only when it reconciles
        // exactly; otherwise retain the single authoritative charge line and let the amount check
        // below fail closed if the provider produces a different total.
        var canShowBreakdown = request.NetAmountMinor > 0 &&
            request.TaxAmountMinor >= 0 &&
            request.CreditConsumedMinor >= 0 &&
            request.NetAmountMinor + request.TaxAmountMinor - request.CreditConsumedMinor ==
            request.AmountMinor;
        var taxLineMinor = canShowBreakdown ? request.TaxAmountMinor : 0;
        var creditLineMinor = canShowBreakdown ? request.CreditConsumedMinor : 0;

        var item = await _invoices.CreateInvoiceItemAsync(
            provider,
            request.ProviderCustomerId!,
            invoice.InvoiceOrItemId!,
            canShowBreakdown ? request.NetAmountMinor : request.AmountMinor,
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

        if (taxLineMinor > 0)
        {
            // Its own idempotency key, derived from the same renewal identity as the line above, so
            // a retried attempt re-creates the same two lines rather than a third.
            var taxItem = await _invoices.CreateInvoiceItemAsync(
                provider,
                request.ProviderCustomerId!,
                invoice.InvoiceOrItemId!,
                taxLineMinor,
                request.CurrencyCode,
                TaxLineDescription(request),
                $"{idempotencyKey}:tax-item",
                cancellationToken);

            if (!taxItem.IsSuccess)
            {
                // Abandoned rather than finalized with the subtotal alone: an invoice missing its
                // tax line would finalize owing less than this renewal is charging, and the amount
                // check below would then void it anyway — with the customer having seen a draft.
                paymentMethodId = string.Empty;
                await _invoices.VoidInvoiceAsync(provider, invoice.InvoiceOrItemId!, cancellationToken);

                return Rejected(taxItem, "subscription_invoice_item_failed", correlationId);
            }
        }

        if (creditLineMinor > 0)
        {
            var creditItem = await _invoices.CreateInvoiceItemAsync(
                provider,
                request.ProviderCustomerId!,
                invoice.InvoiceOrItemId!,
                -creditLineMinor,
                request.CurrencyCode,
                "Subscription credit",
                $"{idempotencyKey}:credit-item",
                cancellationToken);

            if (!creditItem.IsSuccess)
            {
                paymentMethodId = string.Empty;
                await _invoices.VoidInvoiceAsync(provider, invoice.InvoiceOrItemId!, cancellationToken);

                return Rejected(creditItem, "subscription_invoice_item_failed", correlationId);
            }
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
                "abandoned rather than credited ProviderInvoiceId={ProviderInvoiceId} " +
                "ExpectedMinor={ExpectedMinor} InvoicedMinor={InvoicedMinor}",
                PaymentLogValue.Id(invoice.InvoiceOrItemId!),
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
                "ProviderInvoiceId={ProviderInvoiceId}",
                PaymentLogValue.Id(invoice.InvoiceOrItemId!));

            return await RecordSettlementAsync(
                provider,
                request,
                invoice.InvoiceOrItemId!,
                idempotencyKey,
                correlationId,
                cancellationToken);
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
                return await RecordSettlementAsync(
                    provider,
                    request,
                    invoice.InvoiceOrItemId!,
                    idempotencyKey,
                    correlationId,
                    cancellationToken);
            }

            await _invoices.VoidInvoiceAsync(provider, invoice.InvoiceOrItemId!, cancellationToken);

            return Rejected(paid, "subscription_invoice_payment_declined", correlationId);
        }

        _logger.LogInformation(
            "Subscription renewal charged through a Stripe invoice " +
            "ProviderInvoiceId={ProviderInvoiceId} IdempotencyKey={IdempotencyKey} " +
            "CorrelationId={CorrelationId}",
            PaymentLogValue.Id(invoice.InvoiceOrItemId!),
            // The one value that appears in this log line, on the payment we store, and in the
            // provider's own idempotency record — so the three can be joined without guessing which
            // charge was which.
            PaymentLogValue.Id(idempotencyKey),
            PaymentLogValue.Id(correlationId));

        return await RecordSettlementAsync(
            provider,
            request,
            invoice.InvoiceOrItemId!,
            idempotencyKey,
            correlationId,
            cancellationToken);
    }

    /// <summary>
    /// Writes the settled invoice as a payment, and answers with its id.
    /// </summary>
    /// <remarks>
    /// Without this a Stripe renewal left no payment record at all: the invoice id was returned
    /// where a payment id was expected, so a tenant's renewal revenue was invisible to the payment
    /// portal and only first checkouts ever appeared. Every non-Stripe renewal already records one
    /// through <see cref="RecurringChargeBillingGateway"/> — this closes the gap rather than
    /// inventing a second way to account for money.
    /// <para>
    /// Recorded already captured, because that is what it is: an invoice paid at finalization has
    /// no authorize-then-capture step left to drive. The write is best effort — the money has
    /// moved, so a bookkeeping failure must not report a failed renewal and have the next attempt
    /// charge the customer a second time. It degrades to the invoice id and an error worth
    /// alerting on.
    /// </para>
    /// </remarks>
    private async Task<SubscriptionOperationResult<string>> RecordSettlementAsync(
        PaymentProvider provider,
        SubscriptionChargeRequest request,
        string invoiceId,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Its own key, so this record can never collide with one a charge attempt reserved.
        var paymentIdempotencyKey = SubscriptionConstants.RecordedSettlementKeyFor(idempotencyKey);

        try
        {
            if (!_amounts.TryConvertBack(
                    request.AmountMinor,
                    request.CurrencyCode,
                    out var amount))
            {
                _logger.LogError(
                    "A settled subscription invoice could not be recorded because its currency " +
                    "has no configured minor units Currency={Currency}",
                    PaymentLogValue.Label(request.CurrencyCode));

                return SubscriptionOperationResult<string>.Success(invoiceId, correlationId);
            }

            var payment = NewPayment(
                provider,
                request,
                invoiceId,
                paymentIdempotencyKey,
                correlationId,
                amount);

            if (await _payments.TryCreateAsync(payment, cancellationToken))
            {
                return SubscriptionOperationResult<string>.Success(
                    payment.ItemId,
                    correlationId);
            }

            // Already recorded, by a sweep that took the money and then lost its own answer.
            // Returning the existing id keeps a replay pointing at one payment rather than
            // appearing to be a second one.
            var existing = await _payments.GetByIdempotencyKeyAsync(
                request.TenantId,
                paymentIdempotencyKey,
                cancellationToken);

            return SubscriptionOperationResult<string>.Success(
                existing?.ItemId ?? invoiceId,
                correlationId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "A settled subscription invoice could not be recorded as a payment; the money " +
                "was taken and the renewal stands ProviderInvoiceId={ProviderInvoiceId}",
                PaymentLogValue.Id(invoiceId));

            return SubscriptionOperationResult<string>.Success(invoiceId, correlationId);
        }
    }

    private PaymentDetail NewPayment(
        PaymentProvider provider,
        SubscriptionChargeRequest request,
        string invoiceId,
        string paymentIdempotencyKey,
        string correlationId,
        decimal amount)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        return new PaymentDetail
        {
            TenantId = request.TenantId,
            ProviderName = request.ProviderName.ToUpperInvariant(),
            PaymentStatus = PaymentStatuses.Captured,
            PaymentFlow = PaymentFlows.SubscriptionInvoice,
            Amount = (double)amount,
            PreciseAmount = amount,
            CurrencyCode = request.CurrencyCode.ToUpperInvariant(),
            IsRecurring = true,
            RecurringProcessingModel = "Subscription",
            StoredPaymentMethodPublicId = request.StoredPaymentMethodId,

            // The merchant's scope, matching where the provider and card were resolved, so this
            // payment answers provider lookups the same way the charge did.
            OrganizationId = request.OrganizationId,

            // Who the money is for, which is the question reconciliation actually asks.
            CustomerOrganizationId = request.SubscriberOrganizationId,
            CustomerId = request.ProviderCustomerId,
            OrderId = request.OrderId,
            Description = request.Description?.Trim(),
            ProviderInvoiceId = invoiceId,
            SubscriptionNetAmountMinor = request.NetAmountMinor,
            SubscriptionTaxAmountMinor = request.TaxAmountMinor,
            SubscriptionCreditAmountMinor = request.CreditConsumedMinor,
            SubscriptionTaxRateBasisPoints = request.TaxRateBasisPoints,
            SubscriptionTaxMode = request.TaxRateBasisPoints > 0
                ? (request.TaxMode ?? TaxMode.Exclusive).ToString()
                : null,
            // Recorded whenever the caller composed the charge, which is every renewal, plan-change
            // settlement and usage invoice. A gross of zero means nothing was passed, and a null
            // reads back as "this payment predates the breakdown" rather than "nothing came off".
            SubscriptionGrossAmountMinor = request.GrossAmountMinor > 0
                ? request.GrossAmountMinor
                : null,
            SubscriptionBuiltInDiscountMinor = request.GrossAmountMinor > 0
                ? request.BuiltInDiscountMinor
                : null,
            SubscriptionPromotionalDiscountMinor = request.GrossAmountMinor > 0
                ? request.PromotionalDiscountMinor
                : null,
            SubscriptionAutomaticDiscountBasisPoints = request.AutomaticDiscountBasisPoints,
            SubscriptionQuantityDiscountBasisPoints = request.QuantityDiscountBasisPoints,
            SubscriptionDiscountCombination = request.DiscountCombination,
            SubscriptionSettlement = request.Settlement,
            ProviderMerchantAccount = provider.MerchantId,
            MerchantId = provider.MerchantId,
            IdempotencyKey = paymentIdempotencyKey,
            RequestHash = paymentIdempotencyKey,
            CorrelationId = correlationId,
            CreatedAtUtc = now,
            LastUpdatedDateUtc = now,
            PaymentDate = now
        };
    }

    /// <summary>
    /// Stripe's terminal settled status. Compared as an ordinal so a status this code does not
    /// know reads as unpaid rather than as money received.
    /// </summary>
    private static bool IsPaid(string? status) =>
        string.Equals(status, "paid", StringComparison.Ordinal);

    /// <summary>
    /// Names the tax line, with its rate where one is known.
    /// </summary>
    /// <remarks>
    /// The rate is stated rather than left as a bare "Tax" line because an invoice is read by
    /// somebody deciding whether it is right, and 7.7% of the subtotal is the check they will do.
    /// Trailing zeros are trimmed so 20% is not shown as 20.00%.
    /// </remarks>
    private static string TaxLineDescription(SubscriptionChargeRequest request) =>
        request.TaxRateBasisPoints is { } basisPoints && basisPoints > 0
            ? $"Tax ({(basisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture)}%)"
            : "Tax";

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
