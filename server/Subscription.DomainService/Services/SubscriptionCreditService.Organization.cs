using System.Globalization;
using FluentValidation;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Standalone credits (spec 005): bought from a credit offer rather than a plan, held by the
/// organization rather than a subscription, spendable with or without one.
/// </summary>
/// <remarks>
/// Priced here, from the offer the organization resolves; everything after the quote — the
/// record, the code, the charge or checkout, the grant, the recovery sweep — is the same code a
/// subscription purchase runs, with no subscription in hand. What differs is recorded on the
/// purchase: no subscription id, the offer and its version, the meter's name frozen for the
/// invoice, and the billing account the card lives on.
/// </remarks>
public sealed partial class SubscriptionCreditService
{
    private readonly ICreditOfferRepository? _offers;
    private readonly IMeterCatalogueRepository? _catalogue;
    private readonly OrganizationBillingAccountResolver? _organizationAccounts;
    private readonly IValidator<OrganizationCreditPurchaseRequest>? _organizationPurchaseValidator;
    private readonly IValidator<OrganizationGrantCreditsRequest>? _organizationGrantValidator;

    private bool OrganizationCreditsAvailable =>
        _offers is not null && _catalogue is not null && _organizationAccounts is not null &&
        _organizationPurchaseValidator is not null && _organizationGrantValidator is not null;

    public async Task<SubscriptionOperationResult<CreditQuoteResponse>> PreviewForOrganizationAsync(
        OrganizationCreditPurchaseRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var priced = await PriceForOrganizationAsync(request, correlationId, cancellationToken);

        return priced.IsSuccess
            ? SubscriptionOperationResult<CreditQuoteResponse>.Success(
                QuoteResponse(priced.Value!.Purchase), correlationId)
            : priced.ToFailure<CreditQuoteResponse>();
    }

    public async Task<SubscriptionOperationResult<CreditPurchaseResponse>> PurchaseForOrganizationAsync(
        OrganizationCreditPurchaseRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Failure<CreditPurchaseResponse>(
                PaymentFailureKind.Validation,
                "subscription_credits_idempotency_key_required",
                "Send an idempotency key with a purchase, so a retry cannot charge twice.",
                correlationId);
        }

        if (!OrganizationCreditsAvailable)
        {
            return Unavailable<CreditPurchaseResponse>(correlationId);
        }

        var resolution = await _contextResolver.ResolveAsync(correlationId, request.OrganizationId, cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<CreditPurchaseResponse>(correlationId);
        }

        var context = resolution.Context!;
        var purchaseId = SubscriptionCreditPurchase.CreateOrganizationId(context.OrganizationId, request.IdempotencyKey);
        var fingerprint = Fingerprint(request);

        // A retry is answered from the record before anything is re-checked, as a subscription's is.
        if (await _purchases.GetAsync(context.TenantId, purchaseId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(existing, fingerprint, correlationId, cancellationToken);
        }

        var priced = await PriceForOrganizationAsync(context, request, correlationId, cancellationToken);

        if (!priced.IsSuccess)
        {
            return priced.ToFailure<CreditPurchaseResponse>();
        }

        var (purchase, account, discount, label) = priced.Value!;

        return await ExecutePurchaseAsync(
            null, context, purchase, account, discount, purchaseId, request.IdempotencyKey, fingerprint,
            label, correlationId, cancellationToken);
    }

    public async Task<SubscriptionOperationResult<CreditGrantResponse>> GrantToOrganizationAsync(
        OrganizationGrantCreditsRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!OrganizationCreditsAvailable)
        {
            return Unavailable<CreditGrantResponse>(correlationId);
        }

        var invalid = await SubscriptionValidation.CheckAsync<OrganizationGrantCreditsRequest, CreditGrantResponse>(
            _organizationGrantValidator!, request, "subscription_credits_invalid",
            "The credit grant request is invalid.", correlationId, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var resolution = await _contextResolver.ResolveAsync(correlationId, request.OrganizationId, cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<CreditGrantResponse>(correlationId);
        }

        var context = resolution.Context!;
        var offer = await ResolveOfferAsync<CreditGrantResponse>(context, request.MeterKey, forSale: false, correlationId, cancellationToken);

        if (!offer.IsSuccess)
        {
            return offer.ToFailure<CreditGrantResponse>();
        }

        var now = Now();
        var grant = new SubscriptionCreditGrant
        {
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            SubscriptionId = null,
            MeterKey = offer.Value!.Offer.MeterKey,
            Source = CreditGrantSource.AdminGranted,
            Quantity = request.Quantity,
            Remaining = request.Quantity,
            ExpiryPolicy = MeterResetPolicy.Never,
            ExpiresAtUtc = offer.Value.Offer.ExpiresAfter?.EndFrom(now),
            GrantedAtUtc = now,
            GrantedByUserId = context.UserId ?? string.Empty,
            Reason = request.Reason.Trim(),
            LastUpdatedAtUtc = now
        };

        // The event rides in the insert, so the grant and its announcement are one write (spec 005).
        grant.OutboxEvents.Add(CreditLedger.OrganizationEvent(
            grant, SubscriptionConstants.OrganizationCreditsGranted, correlationId, nowUtc: now));
        await _grants.TryInsertAsync(grant, cancellationToken);
        await _ledger.PublishOrganizationAsync(grant, SubscriptionConstants.OrganizationCreditsGranted, correlationId, cancellationToken);
        await AuditAsync(context, null, GrantOperation, "StateApplied", "Succeeded",
            grant.ItemId, null, null, correlationId, cancellationToken, reason: grant.Reason);

        return SubscriptionOperationResult<CreditGrantResponse>.Success(GrantResponse(grant), correlationId);
    }

    public async Task<SubscriptionOperationResult<IReadOnlyList<CreditGrantResponse>>> ListOrganizationGrantsAsync(
        string? organizationId,
        string? meterKey,
        CreditGrantStatus? status,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(correlationId, organizationId, cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<IReadOnlyList<CreditGrantResponse>>(correlationId);
        }

        var context = resolution.Context!;
        var grants = await _grants.ListForOrganizationAsync(
            context.TenantId, context.OrganizationId, meterKey, status, cancellationToken);
        var now = Now();

        return SubscriptionOperationResult<IReadOnlyList<CreditGrantResponse>>.Success(
            [.. grants.Select(grant => GrantResponse(grant, CreditLedger.EffectiveStatus(grant, null, now)))],
            correlationId);
    }

    public async Task<SubscriptionOperationResult<IReadOnlyList<CreditPurchaseResponse>>> ListOrganizationPurchasesAsync(
        string? organizationId,
        CreditPurchaseStatus? status,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(correlationId, organizationId, cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<IReadOnlyList<CreditPurchaseResponse>>(correlationId);
        }

        var context = resolution.Context!;
        var purchases = await _purchases.ListForOrganizationAsync(
            context.TenantId, context.OrganizationId, status, cancellationToken);

        return SubscriptionOperationResult<IReadOnlyList<CreditPurchaseResponse>>.Success(
            [.. purchases.Select(purchase => PurchaseResponse(purchase, null, replayed: false))], correlationId);
    }

    // ---- pricing ------------------------------------------------------------------------------

    /// <summary>The quote and everything it was checked against.</summary>
    private sealed record OrganizationPriced(
        SubscriptionCreditPurchase Purchase,
        BillingAccount Account,
        Discount? Discount,
        string CreditLabel);

    private sealed record ResolvedOffer(CreditOffer Offer, MeterCatalogueEntry? Meter);

    private async Task<SubscriptionOperationResult<OrganizationPriced>> PriceForOrganizationAsync(
        OrganizationCreditPurchaseRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!OrganizationCreditsAvailable)
        {
            return Unavailable<OrganizationPriced>(correlationId);
        }

        var resolution = await _contextResolver.ResolveAsync(correlationId, request.OrganizationId, cancellationToken);

        return resolution.IsSuccess
            ? await PriceForOrganizationAsync(resolution.Context!, request, correlationId, cancellationToken)
            : resolution.ToFailure<OrganizationPriced>(correlationId);
    }

    private async Task<SubscriptionOperationResult<OrganizationPriced>> PriceForOrganizationAsync(
        SubscriptionContext context,
        OrganizationCreditPurchaseRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var invalid = await SubscriptionValidation.CheckAsync<OrganizationCreditPurchaseRequest, OrganizationPriced>(
            _organizationPurchaseValidator!,
            request,
            "subscription_credits_invalid",
            "The credit purchase request is invalid.",
            correlationId,
            cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        // An invoice-billed organization is not sold credits (a purchase is a gateway charge),
        // so a card on its billing account is never charged for them. Checked on the live
        // subscription, the one the contract is; a tenant admin's free grant does not come here.
        if (await _subscriptions.GetLiveAsync(
                context.TenantId, context.OrganizationId, Now(), cancellationToken) is { IsOffline: true })
        {
            return Failure<OrganizationPriced>(
                PaymentFailureKind.Conflict,
                "subscription_offline_collection",
                "This organization is billed by invoice, so credits cannot be bought for it.",
                correlationId);
        }

        var resolved = await ResolveOfferAsync<OrganizationPriced>(context, request.MeterKey, forSale: true, correlationId, cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.ToFailure<OrganizationPriced>();
        }

        var (offer, meter) = resolved.Value!;
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        CreditPack? pack = null;

        if (!string.IsNullOrWhiteSpace(request.PackCode))
        {
            pack = offer.PackByCode(request.PackCode);

            if (pack is null)
            {
                return Failure<OrganizationPriced>(
                    PaymentFailureKind.NotFound,
                    "subscription_credit_pack_not_found",
                    "This offer has no pack with that code.",
                    correlationId);
            }
        }

        // A pack is priced on its own terms and sized by itself; everything else is the offer's.
        var quantity = pack?.Quantity ?? request.Quantity;

        if ((pack is null ? offer.PriceIn(currency) : pack.PriceIn(currency)) is not { } price)
        {
            return Failure<OrganizationPriced>(
                PaymentFailureKind.Conflict,
                "subscription_credits_currency_not_priced",
                pack is null
                    ? $"Credits for this meter are not sold in {currency}."
                    : $"The {pack.DisplayName} pack is not sold in {currency}.",
                correlationId);
        }

        if (pack is null && (quantity < offer.MinQuantity || quantity > offer.MaxQuantity))
        {
            return Failure<OrganizationPriced>(
                PaymentFailureKind.Validation,
                "subscription_credits_quantity_out_of_range",
                $"Buy between {Format(offer.MinQuantity)} and {Format(offer.MaxQuantity)} credits at a time.",
                correlationId);
        }

        var accountResult = await _organizationAccounts!.ResolveAsync(context, correlationId, cancellationToken);

        if (!accountResult.IsSuccess)
        {
            return accountResult.ToFailure<OrganizationPriced>();
        }

        var account = accountResult.Value!;
        var now = Now();
        Discount? discount = null;
        DiscountTerms? discountTerms = null;

        if (!string.IsNullOrWhiteSpace(request.DiscountCode))
        {
            var code = await ResolveDiscountAsync(
                context, string.Empty, currency, offer.MeterKey, request.DiscountCode, now, correlationId, cancellationToken);

            if (!code.IsSuccess)
            {
                return code.ToFailure<OrganizationPriced>();
            }

            (discount, discountTerms) = code.Value;
        }

        var terms = offer.ToTopUpTerms();
        CreditQuote quote;

        try
        {
            quote = CreditQuoteCalculator.Quote(price, terms, quantity, discountTerms, now);
        }
        catch (OverflowException)
        {
            return Failure<OrganizationPriced>(
                PaymentFailureKind.Validation,
                "subscription_credits_amount_too_large",
                "This purchase is larger than can be charged at once. Buy fewer credits.",
                correlationId);
        }

        var floor = _options.CurrentValue.MinimumSettlementChargeMinor;

        if (quote.TotalMinor > 0 && quote.TotalMinor <= floor)
        {
            return Failure<OrganizationPriced>(
                PaymentFailureKind.Validation,
                "subscription_credits_below_minimum_charge",
                $"This purchase comes to {quote.TotalMinor} minor units, which is too small to " +
                    $"charge. Buy enough credits to total more than {floor}.",
                correlationId);
        }

        var path = account is { DefaultPaymentMethodId.Length: > 0 }
            ? CreditPaymentPath.StoredCard
            : CreditPaymentPath.Checkout;

        if (path == CreditPaymentPath.Checkout && (quote.TotalMinor <= 0 || !CheckoutAvailable))
        {
            return Failure<OrganizationPriced>(
                PaymentFailureKind.Conflict,
                "subscription_credits_no_payment_method",
                "There is no saved card to charge. Add a payment method, then buy credits.",
                correlationId);
        }

        var label = meter is { DisplayName.Length: > 0 } ? $"{meter.DisplayName} credits" : $"{offer.MeterKey} credits";

        var purchase = new SubscriptionCreditPurchase
        {
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            SubscriptionId = null,
            OfferId = offer.ItemId,
            OfferVersion = offer.Version,
            PackCode = pack?.Code,
            MeterKey = offer.MeterKey,
            MeterDisplayName = meter?.DisplayName,
            Quantity = quantity,
            BuyerUserId = context.UserId ?? string.Empty,
            CurrencyCode = currency,
            UnitAmountMinor = quote.UnitAmountMinor,
            GrossMinor = quote.GrossMinor,
            DiscountMinor = quote.DiscountMinor,
            NetMinor = quote.NetMinor,
            TaxMinor = quote.TaxMinor,
            TotalMinor = quote.TotalMinor,
            TaxRateBasisPoints = quote.TaxRateBasisPoints,
            TaxMode = quote.TaxMode,
            DiscountCode = discount?.Code,
            DiscountId = discount?.ItemId,
            DiscountVersion = discount?.Version ?? 0,
            // Fixed at the quote, so a later edit to the offer never moves it (AC-15).
            ExpiryPolicy = MeterResetPolicy.Never,
            ExpiresAtUtc = offer.ExpiresAfter?.EndFrom(now),
            PaymentPath = path,
            CreatedAtUtc = now,
            LastUpdatedAtUtc = now
        };

        return SubscriptionOperationResult<OrganizationPriced>.Success(
            new OrganizationPriced(purchase, account, discount, label), correlationId);
    }

    /// <summary>
    /// The offer this organization is served for a meter, with its catalogue entry. A sale needs
    /// the offer enabled and the meter live; a free grant only needs the offer's expiry terms.
    /// </summary>
    private async Task<SubscriptionOperationResult<ResolvedOffer>> ResolveOfferAsync<T>(
        SubscriptionContext context,
        string meterKey,
        bool forSale,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var offer = await _offers!.ResolveAsync(context.TenantId, context.OrganizationId, meterKey, cancellationToken);

        if (offer is null)
        {
            return Failure<ResolvedOffer>(
                PaymentFailureKind.NotFound,
                "subscription_credit_offer_not_found",
                "No credit offer exists for this meter.",
                correlationId);
        }

        var meter = await _catalogue!.ResolveAsync(context.TenantId, context.OrganizationId, meterKey, cancellationToken);

        if (forSale && meter is { Status: CatalogueStatus.Archived })
        {
            return Failure<ResolvedOffer>(
                PaymentFailureKind.Conflict,
                "subscription_meter_catalogue_archived",
                "This meter is archived. Credits already held can still be spent.",
                correlationId);
        }

        if (forSale && !offer.Enabled)
        {
            return Failure<ResolvedOffer>(
                PaymentFailureKind.Conflict,
                "subscription_credits_not_for_sale",
                "Credits for this meter are not for sale at the moment.",
                correlationId);
        }

        return SubscriptionOperationResult<ResolvedOffer>.Success(new ResolvedOffer(offer, meter), correlationId);
    }

    // ---- shared helpers the subscription and organization paths both use --------------------

    private static string Fingerprint(OrganizationCreditPurchaseRequest request) =>
        string.Join(
            '|',
            request.MeterKey,
            Format(request.Quantity),
            request.CurrencyCode.Trim().ToUpperInvariant(),
            request.DiscountCode?.Trim().ToLowerInvariant() ?? string.Empty,
            request.PackCode?.Trim().ToUpperInvariant() ?? string.Empty);

    /// <summary>What the charge and the invoice call the credits, with or without a plan to ask.</summary>
    private static string LabelOf(SubscriptionCreditPurchase purchase, SubscriptionDetail? subscription) =>
        purchase.MeterDisplayName is { Length: > 0 } name
            ? $"{name} credits"
            : subscription is not null && FindMeter(subscription, purchase.MeterKey) is { } meter
                ? CreditLabel(meter)
                : $"{purchase.MeterKey} credits";

    private static string OrderIdFor(SubscriptionDetail? subscription, string organizationId)
    {
        var token = Guid.NewGuid().ToString("N");

        return subscription is null
            ? SubscriptionConstants.OrganizationCreditPurchaseOrderIdFor(organizationId, token)
            : SubscriptionConstants.CreditPurchaseOrderIdFor(subscription.ItemId, token);
    }

    private Task<SubscriptionCreditPurchase?> FindOpenCheckoutAsync(
        string tenantId,
        SubscriptionDetail? subscription,
        string organizationId,
        CancellationToken cancellationToken) =>
        subscription is null
            ? _purchases.FindOpenCheckoutForOrganizationAsync(tenantId, organizationId, cancellationToken)
            : _purchases.FindOpenCheckoutAsync(tenantId, subscription.ItemId, cancellationToken);

    /// <summary>The purchase's subscription as it stands, or null for a standalone purchase or one whose subscription is gone.</summary>
    private async Task<SubscriptionDetail?> SubscriptionOfAsync(
        SubscriptionCreditPurchase purchase,
        CancellationToken cancellationToken) =>
        purchase.SubscriptionId is { Length: > 0 } subscriptionId
            ? await _subscriptions.GetByIdAsync(purchase.TenantId, subscriptionId, cancellationToken)
            : null;

    /// <summary>
    /// The account a paid checkout's card is adopted onto: the one the purchase recorded, or for a
    /// purchase from before that was recorded, the subscription's.
    /// </summary>
    private static string? BillingAccountOf(SubscriptionCreditPurchase purchase, SubscriptionDetail? subscription) =>
        purchase.BillingAccountId is { Length: > 0 } recorded
            ? recorded
            : subscription?.BillingAccountId is { Length: > 0 } own ? own : null;

    private static SubscriptionOperationResult<T> Unavailable<T>(string correlationId) =>
        Failure<T>(
            PaymentFailureKind.Unavailable,
            "subscription_organization_credits_unavailable",
            "Standalone credits are not configured on this host.",
            correlationId);
}
