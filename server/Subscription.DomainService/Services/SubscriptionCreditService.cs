using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Buying, granting, moving and listing meter credits. See spec 002.
/// </summary>
/// <remarks>
/// One service, because every operation that adds credits asks the same questions first: is this
/// subscription in a state that can hold more, does the meter have credit terms, and is the bucket
/// named one this subscription actually has. Asked in one place, a free grant cannot slip past a
/// rule a purchase is held to.
/// <para>
/// A purchase is written down before the card is charged and advanced after each step, so a crash
/// anywhere leaves a record that says how far it got. Every step after the charge can be repeated
/// safely — the grant's id comes from the purchase, the redemption and the outbox event deduplicate
/// on theirs — which is what lets the recovery sweep finish a purchase this service started.
/// </para>
/// </remarks>
public sealed class SubscriptionCreditService : ISubscriptionCreditService
{
    private const string PurchaseOperation = "CreditPurchase";
    private const string GrantOperation = "CreditGrant";
    private const string MoveOperation = "CreditMove";

    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionDiscountRepository _discounts;
    private readonly ICampaignRedemptionRepository _redemptions;
    private readonly ISubscriptionCreditGrantRepository _grants;
    private readonly ISubscriptionCreditPurchaseRepository _purchases;
    private readonly ISubscriptionFinancialDocumentAnnouncer _documents;
    private readonly CreditLedger _ledger;
    private readonly IPaymentRepository _payments;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ISubscriptionAuditTrail _audit;
    private readonly IValidator<CreditPurchaseRequest> _purchaseValidator;
    private readonly IValidator<GrantCreditsRequest> _grantValidator;
    private readonly IValidator<MoveCreditsRequest> _moveValidator;
    private readonly ILogger<SubscriptionCreditService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionCreditService(
        ISubscriptionContextResolver contextResolver,
        ISubscriptionRepository subscriptions,
        IBillingAccountRepository billingAccounts,
        ISubscriptionBillingGateway gateway,
        ISubscriptionDiscountRepository discounts,
        ICampaignRedemptionRepository redemptions,
        ISubscriptionCreditGrantRepository grants,
        ISubscriptionCreditPurchaseRepository purchases,
        ISubscriptionFinancialDocumentAnnouncer documents,
        CreditLedger ledger,
        IPaymentRepository payments,
        IOptionsMonitor<SubscriptionOptions> options,
        ISubscriptionAuditTrail audit,
        IValidator<CreditPurchaseRequest> purchaseValidator,
        IValidator<GrantCreditsRequest> grantValidator,
        IValidator<MoveCreditsRequest> moveValidator,
        ILogger<SubscriptionCreditService> logger,
        TimeProvider? time = null)
    {
        _contextResolver = contextResolver;
        _subscriptions = subscriptions;
        _billingAccounts = billingAccounts;
        _gateway = gateway;
        _discounts = discounts;
        _redemptions = redemptions;
        _grants = grants;
        _purchases = purchases;
        _documents = documents;
        _ledger = ledger;
        _payments = payments;
        _options = options;
        _audit = audit;
        _purchaseValidator = purchaseValidator;
        _grantValidator = grantValidator;
        _moveValidator = moveValidator;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    // ---- preview and purchase -------------------------------------------------------------

    public async Task<SubscriptionOperationResult<CreditQuoteResponse>> PreviewAsync(
        string subscriptionId,
        CreditPurchaseRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var priced = await PriceAsync(subscriptionId, request, correlationId, cancellationToken);

        return priced.IsSuccess
            ? SubscriptionOperationResult<CreditQuoteResponse>.Success(
                QuoteResponse(priced.Value!.Purchase), correlationId)
            : priced.ToFailure<CreditQuoteResponse>();
    }

    public async Task<SubscriptionOperationResult<CreditPurchaseResponse>> PurchaseAsync(
        string subscriptionId,
        CreditPurchaseRequest request,
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

        // A retry is answered from the record before anything is re-checked. The subscription may
        // have moved on since — a cancellation scheduled a minute after paying must not turn the
        // retry of a completed purchase into an error that suggests it never happened.
        var loaded = await LoadAsync(subscriptionId, request.OrganizationId, correlationId, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.ToFailure<CreditPurchaseResponse>();
        }

        var (context, subscription) = loaded.Value!;
        var purchaseId = SubscriptionCreditPurchase.CreateId(subscription.ItemId, request.IdempotencyKey);
        var fingerprint = Fingerprint(request);

        if (await _purchases.GetAsync(context.TenantId, purchaseId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(existing, fingerprint, correlationId, cancellationToken);
        }

        var priced = await PriceAsync(subscription, context, request, correlationId, cancellationToken);

        if (!priced.IsSuccess)
        {
            return priced.ToFailure<CreditPurchaseResponse>();
        }

        var (purchase, meter, account, discount) = priced.Value!;
        purchase.ItemId = purchaseId;
        purchase.IdempotencyKey = request.IdempotencyKey;
        purchase.RequestFingerprint = fingerprint;
        purchase.OrderId = SubscriptionConstants.CreditPurchaseOrderIdFor(
            subscription.ItemId,
            Guid.NewGuid().ToString("N"));
        purchase.ProviderName = account.ProviderName;
        // The merchant's scope, which holds the card, exactly as a renewal charges.
        purchase.ProviderOrganizationId = account.ProviderOrganizationId ?? subscription.OrganizationId;
        purchase.ProviderCustomerId = account.ProviderCustomerId;
        purchase.StoredPaymentMethodId = account.DefaultPaymentMethodId;

        if (!await _purchases.TryCreateAsync(purchase, cancellationToken))
        {
            // Two requests with one key, racing past the read above. The other one owns the charge.
            var winner = await _purchases.GetAsync(context.TenantId, purchaseId, cancellationToken);

            return winner is null
                ? Failure<CreditPurchaseResponse>(
                    PaymentFailureKind.Conflict,
                    "subscription_credits_purchase_in_flight",
                    "This purchase is already being processed. Try again in a moment.",
                    correlationId)
                : await ReplayAsync(winner, fingerprint, correlationId, cancellationToken);
        }

        await AuditAsync(context, subscription, PurchaseOperation, "Started", "InProgress",
            purchase.ItemId, purchase.TotalMinor, purchase.CurrencyCode, correlationId, cancellationToken);

        // Reserved before charging, so two purchases racing for a one-use code cannot both be
        // charged at the discounted price. Released again if the charge does not go through.
        var reserved = false;

        if (discount is { Campaign.OneUsePerOrganization: true })
        {
            var outcome = await _redemptions.TryReserveAsync(
                new CampaignRedemption
                {
                    TenantId = context.TenantId,
                    OrganizationId = subscription.OrganizationId,
                    DiscountId = discount.ItemId,
                    CampaignVersion = discount.Version,
                    SubscriptionId = purchase.ItemId,
                    CreditPurchaseId = purchase.ItemId,
                    OneUsePerOrganization = true,
                    ReservedAtUtc = Now()
                },
                cancellationToken);

            if (outcome == CampaignReservationOutcome.HeldByAnotherSubscription)
            {
                await FailAsync(purchase, "subscription_discount_already_redeemed",
                    "This organization has already redeemed this code.", cancellationToken);

                return Failure<CreditPurchaseResponse>(
                    PaymentFailureKind.Conflict,
                    "subscription_discount_already_redeemed",
                    "This organization has already redeemed this code.",
                    correlationId);
            }

            reserved = true;
        }

        var charged = await ChargeAsync(subscription, meter, purchase, correlationId, cancellationToken);

        if (!charged.IsSuccess && !SettledFailureKinds.Contains(charged.FailureKind))
        {
            // Nobody knows whether the money moved: a timeout can follow an authorization. Failing
            // here would release the code and invite a second purchase while the first one may yet
            // capture, so the purchase stays Pending with its code held and the recovery sweep
            // settles it from what the provider recorded.
            await AuditAsync(context, subscription, PurchaseOperation, "ChargeCompleted", "Unresolved",
                purchase.ItemId, purchase.TotalMinor, purchase.CurrencyCode, correlationId,
                cancellationToken, errorCode: charged.ErrorCode, failureKind: charged.FailureKind.ToString());

            _logger.LogWarning(
                "Credit purchase charge unanswered; left for the recovery sweep PurchaseHash={PurchaseHash} Reason={Reason}",
                PaymentLogValue.Hash(purchase.ItemId),
                PaymentLogValue.Label(charged.ErrorCode ?? "unknown"));

            return Failure<CreditPurchaseResponse>(
                charged.FailureKind,
                "subscription_credits_charge_unresolved",
                "The payment provider did not confirm the charge. The purchase will be finished or " +
                    "cancelled automatically; retry with the same idempotency key to see how it ended.",
                correlationId);
        }

        if (!charged.IsSuccess)
        {
            if (reserved)
            {
                await _redemptions.TryReleaseAsync(
                    context.TenantId, discount!.ItemId, purchase.ItemId, Now(), cancellationToken);
            }

            await FailAsync(purchase, charged.ErrorCode, charged.ErrorMessage, cancellationToken);
            await AuditAsync(context, subscription, PurchaseOperation, "ChargeCompleted", "Failed",
                purchase.ItemId, purchase.TotalMinor, purchase.CurrencyCode, correlationId,
                cancellationToken, errorCode: charged.ErrorCode, failureKind: charged.FailureKind.ToString());

            _logger.LogWarning(
                "Credit purchase declined PurchaseHash={PurchaseHash} Reason={Reason}",
                PaymentLogValue.Hash(purchase.ItemId),
                PaymentLogValue.Label(charged.ErrorCode ?? "unknown"));

            return charged.ToFailure<CreditPurchaseResponse>();
        }

        purchase.PaymentId = charged.Value is { Length: > 0 } paymentId ? paymentId : null;
        purchase.Status = CreditPurchaseStatus.Charged;
        purchase.LastUpdatedAtUtc = Now();

        if (!await _purchases.TryAdvanceAsync(purchase, purchase.Version, cancellationToken))
        {
            // Nothing else writes a purchase this request created, so this is a store that took the
            // charge's record and lost the write. The money has moved: leave it for the recovery
            // sweep rather than granting from a record the store does not agree with.
            _logger.LogError(
                "A charged credit purchase could not be recorded as charged; the recovery sweep " +
                "will finish it PurchaseHash={PurchaseHash}",
                PaymentLogValue.Hash(purchase.ItemId));

            return await ReplayAsync(
                await _purchases.GetAsync(context.TenantId, purchaseId, cancellationToken) ?? purchase,
                fingerprint,
                correlationId,
                cancellationToken);
        }

        var grant = await FinalizeAsync(subscription, purchase, reserved ? discount!.ItemId : null, context, correlationId, cancellationToken);

        return SubscriptionOperationResult<CreditPurchaseResponse>.Success(
            PurchaseResponse(purchase, grant, replayed: false), correlationId);
    }

    /// <summary>
    /// Everything after the charge: the grant, the redemption, the invoice, the event, and the
    /// record marked finished. Each step is safe to repeat.
    /// </summary>
    private async Task<SubscriptionCreditGrant> FinalizeAsync(
        SubscriptionDetail subscription,
        SubscriptionCreditPurchase purchase,
        string? reservedDiscountId,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var grant = new SubscriptionCreditGrant
        {
            // From the purchase, so finishing it twice finds the grant already there.
            ItemId = GrantIdFor(purchase),
            TenantId = purchase.TenantId,
            OrganizationId = subscription.OrganizationId,
            SubscriptionId = subscription.ItemId,
            MeterKey = purchase.MeterKey,
            SeatNumber = purchase.SeatNumber,
            Source = CreditGrantSource.Purchased,
            Quantity = purchase.Quantity,
            Remaining = purchase.Quantity,
            ExpiryPolicy = purchase.ExpiryPolicy,
            ExpiresAtUtc = purchase.ExpiresAtUtc,
            GrantedAtUtc = Now(),
            GrantedByUserId = purchase.BuyerUserId,
            PurchaseId = purchase.ItemId,
            PlanVersionAtGrant = subscription.Plan.PlanVersion,
            LastUpdatedAtUtc = Now()
        };

        if (!await _grants.TryInsertAsync(grant, cancellationToken))
        {
            grant = await _grants.GetAsync(purchase.TenantId, grant.ItemId, cancellationToken) ?? grant;
        }

        if (reservedDiscountId is not null)
        {
            await _redemptions.TryMarkRedeemedAsync(
                purchase.TenantId, reservedDiscountId, purchase.ItemId, Now(), cancellationToken);
        }

        if (purchase.PaymentId is { } paymentId)
        {
            await _documents.AnnounceCreditPurchaseAsync(
                subscription,
                paymentId,
                InvoiceLineFor(subscription, purchase),
                purchase.DiscountCode,
                correlationId,
                cancellationToken,
                SubscriptionDocumentSourceFactory.ActorOf(context.UserId, context.UserName, context.UserEmail));
        }

        await _ledger.PublishAsync(subscription, SubscriptionConstants.SubscriptionCreditsPurchased, grant, correlationId, cancellationToken);

        if (FindMeter(subscription, purchase.MeterKey) is { } meter)
        {
            await _ledger.RearmThresholdsAsync(subscription, meter, purchase.SeatNumber, Now(), cancellationToken);
        }

        purchase.GrantId = grant.ItemId;
        purchase.Status = CreditPurchaseStatus.Finalized;
        purchase.LastUpdatedAtUtc = Now();
        await _purchases.TryAdvanceAsync(purchase, purchase.Version, cancellationToken);

        await AuditAsync(context, subscription, PurchaseOperation, "StateApplied", "Succeeded",
            purchase.ItemId, purchase.TotalMinor, purchase.CurrencyCode, correlationId,
            cancellationToken, paymentDetailId: purchase.PaymentId);

        return grant;
    }

    /// <remarks>
    /// Routed from the purchase, never from the billing account as it stands now, so the recovery
    /// sweep's replay reaches the same provider customer and card the first attempt did.
    /// </remarks>
    private async Task<SubscriptionOperationResult<string>> ChargeAsync(
        SubscriptionDetail subscription,
        PlanMeter? meter,
        SubscriptionCreditPurchase purchase,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // A code can take a purchase to nothing. There is then nothing to collect and nothing for
        // an invoice to describe, the same way a renewal priced at zero skips the provider.
        if (purchase.TotalMinor <= 0)
        {
            return SubscriptionOperationResult<string>.Success(string.Empty, correlationId);
        }

        return await _gateway.ChargeAsync(
            new SubscriptionChargeRequest
            {
                TenantId = subscription.TenantId,
                OrganizationId = purchase.ProviderOrganizationId ?? subscription.OrganizationId,
                SubscriberOrganizationId = subscription.OrganizationId,
                ProviderName = purchase.ProviderName,
                StoredPaymentMethodId = purchase.StoredPaymentMethodId!,
                ProviderCustomerId = purchase.ProviderCustomerId,
                AmountMinor = purchase.TotalMinor,
                NetAmountMinor = purchase.NetMinor,
                TaxAmountMinor = purchase.TaxMinor,
                TaxRateBasisPoints = purchase.TaxRateBasisPoints,
                TaxMode = purchase.TaxMode,
                GrossAmountMinor = purchase.GrossMinor,
                PromotionalDiscountMinor = purchase.DiscountMinor,
                CurrencyCode = purchase.CurrencyCode,
                OrderId = purchase.OrderId,
                Description = $"{Format(purchase.Quantity)} {(meter is null ? $"{purchase.MeterKey} credits" : CreditLabel(meter))}"
            },
            SubscriptionConstants.CreditPurchaseChargeKeyFor(purchase.ItemId),
            correlationId,
            cancellationToken);
    }

    /// <summary>
    /// Answers a retry with what the first request produced, or refuses a key reused for a
    /// different purchase.
    /// </summary>
    private async Task<SubscriptionOperationResult<CreditPurchaseResponse>> ReplayAsync(
        SubscriptionCreditPurchase existing,
        string fingerprint,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return Failure<CreditPurchaseResponse>(
                PaymentFailureKind.Conflict,
                "subscription_credits_idempotency_conflict",
                "This idempotency key was already used for a different purchase. Use a new key " +
                    "for a new purchase.",
                correlationId);
        }

        if (existing.Status == CreditPurchaseStatus.Failed)
        {
            return Failure<CreditPurchaseResponse>(
                PaymentFailureKind.ProviderRejected,
                existing.FailureCode ?? "subscription_credits_purchase_failed",
                existing.FailureReason ?? "This purchase did not go through.",
                correlationId);
        }

        var grant = existing.GrantId is { } grantId
            ? await _grants.GetAsync(existing.TenantId, grantId, cancellationToken)
            : null;

        return SubscriptionOperationResult<CreditPurchaseResponse>.Success(
            PurchaseResponse(existing, grant, replayed: true), correlationId);
    }

    private async Task FailAsync(
        SubscriptionCreditPurchase purchase,
        string? code,
        string? reason,
        CancellationToken cancellationToken)
    {
        purchase.Status = CreditPurchaseStatus.Failed;
        purchase.FailureCode = code;
        purchase.FailureReason = reason;
        purchase.LastUpdatedAtUtc = Now();
        await _purchases.TryAdvanceAsync(purchase, purchase.Version, cancellationToken);
    }

    /// <summary>The quote and everything it was checked against, for a preview or a purchase.</summary>
    private sealed record Priced(
        SubscriptionCreditPurchase Purchase,
        PlanMeter Meter,
        BillingAccount Account,
        Discount? Discount);

    private async Task<SubscriptionOperationResult<Priced>> PriceAsync(
        string subscriptionId,
        CreditPurchaseRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(subscriptionId, request.OrganizationId, correlationId, cancellationToken);

        return loaded.IsSuccess
            ? await PriceAsync(loaded.Value!.Subscription, loaded.Value.Context, request, correlationId, cancellationToken)
            : loaded.ToFailure<Priced>();
    }

    private async Task<SubscriptionOperationResult<Priced>> PriceAsync(
        SubscriptionDetail subscription,
        SubscriptionContext context,
        CreditPurchaseRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var invalid = await SubscriptionValidation.CheckAsync<CreditPurchaseRequest, Priced>(
            _purchaseValidator,
            request,
            "subscription_credits_invalid",
            "The credit purchase request is invalid.",
            correlationId,
            cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        if (RefuseLifecycle<Priced>(subscription, correlationId) is { } refused)
        {
            return refused;
        }

        if (FindMeter(subscription, request.MeterKey) is not { } meter)
        {
            return MeterNotFound<Priced>(correlationId);
        }

        if (meter.TopUp is not { Enabled: true } terms)
        {
            return Failure<Priced>(
                PaymentFailureKind.Conflict,
                "subscription_credits_not_for_sale",
                "Credits for this meter are not for sale on this subscription's plan.",
                correlationId);
        }

        if (terms.PriceIn(subscription.CurrencyCode) is not { } price)
        {
            return Failure<Priced>(
                PaymentFailureKind.Conflict,
                "subscription_credits_currency_not_priced",
                $"Credits for this meter are not sold in {subscription.CurrencyCode}.",
                correlationId);
        }

        if (request.Quantity < terms.MinQuantity || request.Quantity > terms.MaxQuantity)
        {
            return Failure<Priced>(
                PaymentFailureKind.Validation,
                "subscription_credits_quantity_out_of_range",
                $"Buy between {Format(terms.MinQuantity)} and {Format(terms.MaxQuantity)} credits at a time.",
                correlationId);
        }

        if (RefuseBucket<Priced>(subscription, request.SeatNumber, correlationId) is { } badSeat)
        {
            return badSeat;
        }

        var account = await _billingAccounts.GetAsync(
            subscription.TenantId, subscription.BillingAccountId, cancellationToken);

        if (account is not { DefaultPaymentMethodId.Length: > 0 })
        {
            return Failure<Priced>(
                PaymentFailureKind.Conflict,
                "subscription_credits_no_payment_method",
                "There is no saved card to charge. Add a payment method to the subscription, then " +
                    "buy credits.",
                correlationId);
        }

        var now = Now();
        Discount? discount = null;
        DiscountTerms? discountTerms = null;

        if (!string.IsNullOrWhiteSpace(request.DiscountCode))
        {
            var resolved = await ResolveDiscountAsync(
                context, subscription, meter, request.DiscountCode, now, correlationId, cancellationToken);

            if (!resolved.IsSuccess)
            {
                return resolved.ToFailure<Priced>();
            }

            (discount, discountTerms) = resolved.Value;
        }

        CreditQuote quote;

        try
        {
            quote = CreditQuoteCalculator.Quote(price, terms, request.Quantity, discountTerms, now);
        }
        catch (OverflowException)
        {
            return Failure<Priced>(
                PaymentFailureKind.Validation,
                "subscription_credits_amount_too_large",
                "This purchase is larger than can be charged at once. Buy fewer credits.",
                correlationId);
        }

        var purchase = new SubscriptionCreditPurchase
        {
            TenantId = subscription.TenantId,
            OrganizationId = subscription.OrganizationId,
            SubscriptionId = subscription.ItemId,
            MeterKey = meter.MeterKey,
            SeatNumber = request.SeatNumber,
            Quantity = request.Quantity,
            BuyerUserId = context.UserId ?? string.Empty,
            CurrencyCode = subscription.CurrencyCode,
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
            ExpiryPolicy = terms.ExpiryPolicy,
            ExpiresAtUtc = ExpiryOf(subscription, meter, terms.ExpiryPolicy, now),
            CreatedAtUtc = now,
            LastUpdatedAtUtc = now
        };

        return SubscriptionOperationResult<Priced>.Success(
            new Priced(purchase, meter, account, discount), correlationId);
    }

    /// <summary>
    /// The code, checked the way signup checks one, against a credit purchase instead of a plan.
    /// </summary>
    private async Task<SubscriptionOperationResult<(Discount, DiscountTerms)>> ResolveDiscountAsync(
        SubscriptionContext context,
        SubscriptionDetail subscription,
        PlanMeter meter,
        string code,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var discount = await _discounts.FindActiveByCodeAsync(
            context.TenantId, context.OrganizationId, code.Trim().ToLowerInvariant(), cancellationToken);

        if (discount is null)
        {
            return Failure<(Discount, DiscountTerms)>(
                PaymentFailureKind.NotFound,
                "subscription_discount_not_found",
                "The discount code does not exist or is retired.",
                correlationId);
        }

        if (discount.Terms.StartsAtUtc is { } startsAt && now < startsAt)
        {
            return Failure<(Discount, DiscountTerms)>(
                PaymentFailureKind.Validation,
                "subscription_discount_not_started",
                "The discount code has not started yet.",
                correlationId);
        }

        if (discount.Terms.ExpiresAtUtc is { } expiry && expiry <= now)
        {
            return Failure<(Discount, DiscountTerms)>(
                PaymentFailureKind.Validation,
                "subscription_discount_expired",
                "The discount code has expired.",
                correlationId);
        }

        if (!SubscriptionDiscountApplicability.PermitsCreditPurchase(discount, subscription.Plan.Code, meter.MeterKey))
        {
            return Failure<(Discount, DiscountTerms)>(
                PaymentFailureKind.Validation,
                "subscription_discount_not_applicable",
                "This code does not apply to credits for this meter.",
                correlationId);
        }

        if (discount.Terms.Kind == DiscountKind.FixedAmount &&
            !string.Equals(discount.CurrencyCode, subscription.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return Failure<(Discount, DiscountTerms)>(
                PaymentFailureKind.Validation,
                "subscription_discount_currency_mismatch",
                "The fixed discount is denominated in another currency.",
                correlationId);
        }

        if (discount.Campaign.OneUsePerOrganization &&
            await _redemptions.FindActiveForOrganizationAsync(
                context.TenantId, subscription.OrganizationId, discount.ItemId, cancellationToken) is not null)
        {
            return Failure<(Discount, DiscountTerms)>(
                PaymentFailureKind.Conflict,
                "subscription_discount_already_redeemed",
                "This organization has already redeemed this code.",
                correlationId);
        }

        return SubscriptionOperationResult<(Discount, DiscountTerms)>.Success(
            (discount, new DiscountTerms
            {
                Code = discount.Terms.Code,
                Kind = discount.Terms.Kind,
                PercentBasisPoints = discount.Terms.PercentBasisPoints,
                AmountMinor = discount.Terms.AmountMinor,
                StartsAtUtc = discount.Terms.StartsAtUtc,
                ExpiresAtUtc = discount.Terms.ExpiresAtUtc,
                DiscountId = discount.ItemId,
                DiscountVersion = discount.Version,
                RedeemedAtUtc = now
            }),
            correlationId);
    }

    // ---- free grants and moves ------------------------------------------------------------

    public async Task<SubscriptionOperationResult<CreditGrantResponse>> GrantAsync(
        string subscriptionId,
        GrantCreditsRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = await SubscriptionValidation.CheckAsync<GrantCreditsRequest, CreditGrantResponse>(
            _grantValidator, request, "subscription_credits_invalid",
            "The credit grant request is invalid.", correlationId, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var loaded = await LoadAsync(subscriptionId, request.OrganizationId, correlationId, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.ToFailure<CreditGrantResponse>();
        }

        var (context, subscription) = loaded.Value!;

        if (RefuseLifecycle<CreditGrantResponse>(subscription, correlationId) is { } refused)
        {
            return refused;
        }

        if (FindMeter(subscription, request.MeterKey) is not { } meter)
        {
            return MeterNotFound<CreditGrantResponse>(correlationId);
        }

        // Terms are required, not sales: the grant takes its expiry from them, and an author who
        // has paused sales may still want support to hand some out.
        if (meter.TopUp is not { } terms)
        {
            return Failure<CreditGrantResponse>(
                PaymentFailureKind.Conflict,
                "subscription_credits_no_terms",
                "This meter has no credit terms, so there is nothing to say when granted credits " +
                    "would expire. Configure credits for the meter on the plan first.",
                correlationId);
        }

        if (RefuseBucket<CreditGrantResponse>(subscription, request.SeatNumber, correlationId) is { } badSeat)
        {
            return badSeat;
        }

        var now = Now();
        var grant = new SubscriptionCreditGrant
        {
            TenantId = subscription.TenantId,
            OrganizationId = subscription.OrganizationId,
            SubscriptionId = subscription.ItemId,
            MeterKey = meter.MeterKey,
            SeatNumber = request.SeatNumber,
            Source = CreditGrantSource.AdminGranted,
            Quantity = request.Quantity,
            Remaining = request.Quantity,
            ExpiryPolicy = terms.ExpiryPolicy,
            ExpiresAtUtc = ExpiryOf(subscription, meter, terms.ExpiryPolicy, now),
            GrantedAtUtc = now,
            GrantedByUserId = context.UserId ?? string.Empty,
            Reason = request.Reason.Trim(),
            PlanVersionAtGrant = subscription.Plan.PlanVersion,
            LastUpdatedAtUtc = now
        };

        await _grants.TryInsertAsync(grant, cancellationToken);
        await _ledger.PublishAsync(subscription, SubscriptionConstants.SubscriptionCreditsGranted, grant, correlationId, cancellationToken);
        await _ledger.RearmThresholdsAsync(subscription, meter, grant.SeatNumber, now, cancellationToken);
        await AuditAsync(context, subscription, GrantOperation, "StateApplied", "Succeeded",
            grant.ItemId, null, null, correlationId, cancellationToken, reason: grant.Reason);

        return SubscriptionOperationResult<CreditGrantResponse>.Success(GrantResponse(grant), correlationId);
    }

    public async Task<SubscriptionOperationResult<CreditMoveResponse>> MoveAsync(
        string subscriptionId,
        MoveCreditsRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = await SubscriptionValidation.CheckAsync<MoveCreditsRequest, CreditMoveResponse>(
            _moveValidator, request, "subscription_credits_invalid",
            "The credit move request is invalid.", correlationId, cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var loaded = await LoadAsync(subscriptionId, request.OrganizationId, correlationId, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.ToFailure<CreditMoveResponse>();
        }

        var (context, subscription) = loaded.Value!;

        if (RefuseLifecycle<CreditMoveResponse>(subscription, correlationId) is { } refused)
        {
            return refused;
        }

        if (FindMeter(subscription, request.MeterKey) is not { } meter)
        {
            return MeterNotFound<CreditMoveResponse>(correlationId);
        }

        if (subscription.Plan.SubscriberScope != SubscriberScope.User)
        {
            return Failure<CreditMoveResponse>(
                PaymentFailureKind.Validation,
                "subscription_not_member_based",
                "This subscription belongs to the organization itself, so it has no seats to move " +
                    "credits between.",
                correlationId);
        }

        if (RefuseBucket<CreditMoveResponse>(subscription, request.FromSeatNumber, correlationId) is { } badFrom)
        {
            return badFrom;
        }

        if (RefuseBucket<CreditMoveResponse>(subscription, request.ToSeatNumber, correlationId) is { } badTo)
        {
            return badTo;
        }

        var now = Now();
        var live = await _ledger.SpendableAsync(
            subscription, meter.MeterKey, request.FromSeatNumber, now, cancellationToken);

        if (live.Sum(grant => grant.Remaining) < request.Quantity)
        {
            return Failure<CreditMoveResponse>(
                PaymentFailureKind.Conflict,
                "subscription_credits_insufficient",
                "There are not that many unused credits to move.",
                correlationId);
        }

        // Taken in the order they would be spent, so a move hands over the credits that would have
        // lapsed first and leaves the giver the longest-lasting ones. Each piece keeps its own grant's
        // expiry: moving credits must never extend or shorten them.
        //
        // From the first debit on, the move is finished or undone whatever the caller does, so no
        // write below listens for the caller's cancellation: a disconnect between a debit and its
        // insert would otherwise delete the credits. ponytail: a process killed in that same gap
        // still loses the piece; a move record swept like purchases closes it if that ever matters.
        var write = CancellationToken.None;
        var taken = new List<(SubscriptionCreditGrant After, decimal Amount)>();
        var left = request.Quantity;

        foreach (var grant in live)
        {
            if (left <= 0)
            {
                break;
            }

            var amount = Math.Min(grant.Remaining, left);

            if (await _grants.TryConsumeAsync(subscription.TenantId, grant.ItemId, amount, now, write)
                is not { } after)
            {
                // Spent or closed since it was listed. Put back what this move already took so a
                // refused move changes nothing, and let the caller try again with fresh figures.
                await RestoreTakenAsync(subscription.TenantId, taken, now);

                return Failure<CreditMoveResponse>(
                    PaymentFailureKind.Conflict,
                    "subscription_credits_changed",
                    "These credits changed while being moved. Reload them and try again.",
                    correlationId);
            }

            taken.Add((after, amount));
            left -= amount;
        }

        var created = new List<SubscriptionCreditGrant>(taken.Count);

        try
        {
            foreach (var (source, amount) in taken)
            {
                created.Add(await InsertMovedAsync(subscription, meter, request, context, source, amount, now, write));
            }
        }
        catch
        {
            // What arrived stays moved; every piece that did not goes back to its grant. No credit
            // is lost and none is counted twice, whichever insert failed.
            await RestoreTakenAsync(subscription.TenantId, taken.Skip(created.Count), now);
            throw;
        }

        foreach (var (moved, (source, _)) in created.Zip(taken))
        {
            await _ledger.PublishAsync(subscription, SubscriptionConstants.SubscriptionCreditsMoved, moved,
                correlationId, write, request.FromSeatNumber);

            if (source.Status == CreditGrantStatus.Exhausted)
            {
                await _ledger.PublishAsync(subscription, SubscriptionConstants.SubscriptionCreditsExhausted, source,
                    correlationId, write);
            }
        }

        await _ledger.RearmThresholdsAsync(subscription, meter, request.ToSeatNumber, now, write);

        await AuditAsync(context, subscription, MoveOperation, "StateApplied", "Succeeded",
            string.Join(',', created.Select(grant => grant.ItemId)), null, null, correlationId, write);

        return SubscriptionOperationResult<CreditMoveResponse>.Success(
            new CreditMoveResponse
            {
                From = [.. taken.Select(piece => GrantResponse(piece.After))],
                To = [.. created.Select(GrantResponse)]
            },
            correlationId);
    }

    /// <summary>One moved piece, carrying its source grant's expiry. Throws if it cannot be stored.</summary>
    private async Task<SubscriptionCreditGrant> InsertMovedAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        MoveCreditsRequest request,
        SubscriptionContext context,
        SubscriptionCreditGrant source,
        decimal amount,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var moved = new SubscriptionCreditGrant
        {
            TenantId = subscription.TenantId,
            OrganizationId = subscription.OrganizationId,
            SubscriptionId = subscription.ItemId,
            MeterKey = meter.MeterKey,
            SeatNumber = request.ToSeatNumber,
            Source = CreditGrantSource.Moved,
            Quantity = amount,
            Remaining = amount,
            ExpiryPolicy = source.ExpiryPolicy,
            ExpiresAtUtc = source.ExpiresAtUtc,
            GrantedAtUtc = now,
            GrantedByUserId = context.UserId ?? string.Empty,
            SourceGrantId = source.ItemId,
            PlanVersionAtGrant = source.PlanVersionAtGrant,
            LastUpdatedAtUtc = now
        };

        // A fresh id, so a false here is a store that refused the write, not an earlier copy.
        if (!await _grants.TryInsertAsync(moved, cancellationToken))
        {
            throw new InvalidOperationException("A moved credit grant could not be stored.");
        }

        return moved;
    }

    private async Task RestoreTakenAsync(
        string tenantId,
        IEnumerable<(SubscriptionCreditGrant After, decimal Amount)> taken,
        DateTime now)
    {
        foreach (var (grant, amount) in taken)
        {
            await _grants.RestoreAsync(tenantId, grant.ItemId, amount, now, CancellationToken.None);
        }
    }

    // ---- lists ----------------------------------------------------------------------------

    public async Task<SubscriptionOperationResult<IReadOnlyList<CreditGrantResponse>>> ListGrantsAsync(
        string subscriptionId,
        string? organizationId,
        string? meterKey,
        int? seatNumber,
        bool poolOnly,
        CreditGrantStatus? status,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(subscriptionId, organizationId, correlationId, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.ToFailure<IReadOnlyList<CreditGrantResponse>>();
        }

        var subscription = loaded.Value!.Subscription;
        var grants = await _grants.ListAsync(
            subscription.TenantId, subscription.ItemId, meterKey, seatNumber, poolOnly, status, cancellationToken);

        // The status as it is now, not as the maintenance sweep last stored it: a grant forfeited by
        // a plan change an hour ago must not read Active until tomorrow's sweep.
        var now = Now();

        return SubscriptionOperationResult<IReadOnlyList<CreditGrantResponse>>.Success(
            [.. grants.Select(grant => GrantResponse(grant, CreditLedger.EffectiveStatus(grant, subscription, now)))],
            correlationId);
    }

    public async Task<SubscriptionOperationResult<IReadOnlyList<CreditPurchaseResponse>>> ListPurchasesAsync(
        string subscriptionId,
        string? organizationId,
        CreditPurchaseStatus? status,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(subscriptionId, organizationId, correlationId, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.ToFailure<IReadOnlyList<CreditPurchaseResponse>>();
        }

        var subscription = loaded.Value!.Subscription;
        var purchases = await _purchases.ListAsync(subscription.TenantId, subscription.ItemId, status, cancellationToken);

        return SubscriptionOperationResult<IReadOnlyList<CreditPurchaseResponse>>.Success(
            [.. purchases.Select(purchase => PurchaseResponse(purchase, null, replayed: false))], correlationId);
    }

    // ---- recovery -------------------------------------------------------------------------

    private static readonly string[] ChargedStatuses =
    [
        PaymentStatuses.Authorized,
        PaymentStatuses.Captured,
        PaymentStatuses.PartiallyCaptured
    ];

    private static readonly string[] DeclinedStatuses =
    [
        PaymentStatuses.Refused,
        PaymentStatuses.Cancelled,
        PaymentStatuses.MakePaymentFailed,
        PaymentStatuses.Expired
    ];

    /// <summary>
    /// The failures that are an answer: the provider refused, or the request never left. Anything
    /// else (a timeout, an outage) leaves open whether the money moved.
    /// </summary>
    private static readonly PaymentFailureKind[] SettledFailureKinds =
    [
        PaymentFailureKind.ProviderRejected,
        PaymentFailureKind.Validation,
        PaymentFailureKind.NotFound,
        PaymentFailureKind.Conflict,
        PaymentFailureKind.RateLimited
    ];

    private static readonly CreditPurchaseStatus[] Unfinished =
        [CreditPurchaseStatus.Pending, CreditPurchaseStatus.Charged];

    /// <summary>
    /// Finishes or fails the purchases a crash left part way, for one tenant. Returns how many it
    /// settled one way or the other.
    /// </summary>
    /// <remarks>
    /// A Charged purchase took the money, so it is always finished: the steps after the charge are
    /// all safe to repeat. A Pending one is settled by what the payment module recorded under the
    /// purchase's charge key. A recurring card charge is recorded there before the provider is
    /// called, so finding nothing means no money moved and the purchase can fail and give its code
    /// back. A Stripe invoice is recorded only once paid, so for Stripe finding nothing proves
    /// nothing: the charge is replayed under its own key, which the provider answers with the
    /// original charge if there was one. One still in flight is left alone until the alert
    /// deadline, then handed to an operator rather than guessed at.
    /// </remarks>
    public async Task<int> RecoverStaleAsync(string tenantId, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = Now();
        var stale = await _purchases.ListStaleAsync(
            tenantId,
            Unfinished,
            now.AddSeconds(-Math.Max(30, options.CreditPurchaseRecoveryAfterSeconds)),
            Math.Max(1, options.CreditMaintenanceBatchSize),
            cancellationToken);
        var settled = 0;

        foreach (var purchase in stale)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (await RecoverAsync(purchase, now, options, cancellationToken))
            {
                settled++;
            }
        }

        return settled;
    }

    private async Task<bool> RecoverAsync(
        SubscriptionCreditPurchase purchase,
        DateTime now,
        SubscriptionOptions options,
        CancellationToken cancellationToken)
    {
        var correlationId = $"credit-recovery-{Guid.NewGuid():N}";
        var overdue = now - purchase.CreatedAtUtc > TimeSpan.FromHours(Math.Max(1, options.CreditPurchaseAlertAfterHours));

        if (purchase.Status == CreditPurchaseStatus.Pending)
        {
            var payment = await FindChargeAsync(purchase, cancellationToken);
            string? paymentId;

            if (payment is null &&
                string.Equals(purchase.ProviderName, PaymentConstants.StripeProvider, StringComparison.OrdinalIgnoreCase))
            {
                var replayed = await ReplayChargeAsync(purchase, correlationId, cancellationToken);

                if (replayed is null)
                {
                    return await HoldAsync(purchase, now, overdue, cancellationToken);
                }

                if (!replayed.IsSuccess)
                {
                    // The provider answered, and its answer is that no money moved.
                    if (purchase.DiscountId is { } refusedDiscountId)
                    {
                        await _redemptions.TryReleaseAsync(
                            purchase.TenantId, refusedDiscountId, purchase.ItemId, now, cancellationToken);
                    }

                    await FailAsync(purchase, replayed.ErrorCode ?? "subscription_credits_charge_declined",
                        replayed.ErrorMessage ?? "The card was not charged.", cancellationToken);
                    return true;
                }

                paymentId = replayed.Value;
            }
            else if (payment is null || DeclinedStatuses.Contains(payment.PaymentStatus, StringComparer.Ordinal))
            {
                if (purchase.DiscountId is { } discountId)
                {
                    await _redemptions.TryReleaseAsync(
                        purchase.TenantId, discountId, purchase.ItemId, now, cancellationToken);
                }

                await FailAsync(
                    purchase,
                    payment is null ? "subscription_credits_charge_not_found" : "subscription_credits_charge_declined",
                    payment is null
                        ? "The purchase was interrupted before the card was charged."
                        : "The card was not charged.",
                    cancellationToken);
                return true;
            }

            else if (!ChargedStatuses.Contains(payment.PaymentStatus, StringComparer.Ordinal))
            {
                return await HoldAsync(purchase, now, overdue, cancellationToken);
            }
            else
            {
                paymentId = payment.ItemId;
            }

            purchase.PaymentId = paymentId is { Length: > 0 } ? paymentId : null;
            purchase.Status = CreditPurchaseStatus.Charged;
            purchase.LastUpdatedAtUtc = now;

            if (!await _purchases.TryAdvanceAsync(purchase, purchase.Version, cancellationToken))
            {
                return false;
            }
        }

        var subscription = await _subscriptions.GetByIdAsync(purchase.TenantId, purchase.SubscriptionId, cancellationToken);

        if (subscription is null)
        {
            // Paid for, with nothing left to grant into. Never dropped silently: this is money taken.
            return await HoldAsync(purchase, now, overdue: true, cancellationToken);
        }

        await FinalizeAsync(
            subscription,
            purchase,
            purchase.DiscountId,
            new SubscriptionContext(purchase.TenantId, purchase.OrganizationId, "credit-purchase-recovery", purchase.BuyerUserId),
            correlationId,
            cancellationToken);

        _logger.LogInformation(
            "Credit purchase finished by the recovery sweep PurchaseHash={PurchaseHash}",
            PaymentLogValue.Hash(purchase.ItemId));

        return true;
    }

    /// <summary>
    /// Leaves a purchase for the next sweep, or past the deadline marks it for an operator. Touched
    /// either way, so one stuck purchase cannot hold the front of every batch.
    /// </summary>
    private async Task<bool> HoldAsync(
        SubscriptionCreditPurchase purchase,
        DateTime now,
        bool overdue,
        CancellationToken cancellationToken)
    {
        purchase.LastUpdatedAtUtc = now;

        if (overdue)
        {
            purchase.Status = CreditPurchaseStatus.NeedsAttention;
            _logger.LogError(
                "A credit purchase could not be settled and needs an operator PurchaseHash={PurchaseHash} " +
                "PaymentHash={PaymentHash}",
                PaymentLogValue.Hash(purchase.ItemId),
                PaymentLogValue.Hash(purchase.PaymentId ?? string.Empty));
        }

        await _purchases.TryAdvanceAsync(purchase, purchase.Version, cancellationToken);
        return overdue;
    }

    /// <summary>
    /// Re-raises a purchase's charge under its own key and routing. Null when there is no answer
    /// either way (no card to replay with, no subscription to bill, or the provider still not
    /// responding), so the purchase is held rather than guessed at.
    /// </summary>
    private async Task<SubscriptionOperationResult<string>?> ReplayChargeAsync(
        SubscriptionCreditPurchase purchase,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(purchase.StoredPaymentMethodId))
        {
            return null;
        }

        var subscription = await _subscriptions.GetByIdAsync(purchase.TenantId, purchase.SubscriptionId, cancellationToken);

        if (subscription is null)
        {
            return null;
        }

        var charged = await ChargeAsync(
            subscription, FindMeter(subscription, purchase.MeterKey), purchase, correlationId, cancellationToken);

        return charged.IsSuccess || SettledFailureKinds.Contains(charged.FailureKind) ? charged : null;
    }

    /// <summary>The payment this purchase's charge was recorded as, looked up the way settlements are.</summary>
    private async Task<PaymentDetail?> FindChargeAsync(
        SubscriptionCreditPurchase purchase,
        CancellationToken cancellationToken)
    {
        var chargeKey = SubscriptionConstants.CreditPurchaseChargeKeyFor(purchase.ItemId);

        return await _payments.GetByIdempotencyKeyAsync(purchase.TenantId, chargeKey, cancellationToken)
               ?? await _payments.GetByIdempotencyKeyAsync(
                   purchase.TenantId,
                   SubscriptionConstants.RecordedSettlementKeyFor(chargeKey),
                   cancellationToken);
    }

    // ---- shared checks --------------------------------------------------------------------

    private async Task<SubscriptionOperationResult<(SubscriptionContext Context, SubscriptionDetail Subscription)>> LoadAsync(
        string subscriptionId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(correlationId, organizationId, cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<(SubscriptionContext, SubscriptionDetail)>(correlationId);
        }

        var context = resolution.Context!;

        // Through the organization, never by id alone, so naming somebody else's subscription finds
        // nothing rather than their credits.
        var subscription = await _subscriptions.GetAsync(
            context.TenantId, context.OrganizationId, subscriptionId, cancellationToken);

        return subscription is null
            ? Failure<(SubscriptionContext, SubscriptionDetail)>(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "That subscription does not exist for this organization.",
                correlationId)
            : SubscriptionOperationResult<(SubscriptionContext, SubscriptionDetail)>.Success(
                (context, subscription), correlationId);
    }

    /// <summary>
    /// The states no credits may be added in, each with the reason a subscriber can act on.
    /// </summary>
    /// <remarks>
    /// The trial and the scheduled cancellation come first and are named, because they are the
    /// two a subscriber is most likely to be in and can most easily change: convert the trial, or
    /// undo the cancellation. Every other non-active state shares one answer.
    /// </remarks>
    private static SubscriptionOperationResult<T>? RefuseLifecycle<T>(
        SubscriptionDetail subscription,
        string correlationId)
    {
        if (subscription.Status == SubscriptionStatus.Trialing)
        {
            return Failure<T>(
                PaymentFailureKind.Conflict,
                "subscription_credits_trialing",
                "Credits can be bought once the trial has converted to a paid subscription.",
                correlationId);
        }

        if (subscription.CancelAtPeriodEnd)
        {
            return Failure<T>(
                PaymentFailureKind.Conflict,
                "subscription_credits_cancellation_scheduled",
                "Credits cannot be bought while a cancellation is scheduled. Undo the cancellation " +
                    "first.",
                correlationId);
        }

        return subscription.Status == SubscriptionStatus.Active
            ? null
            : Failure<T>(
                PaymentFailureKind.Conflict,
                "subscription_credits_not_allowed",
                "Credits can only be added to an active subscription.",
                correlationId);
    }

    /// <summary>Whether the bucket named exists on this subscription. Null is the pool, which always does.</summary>
    private static SubscriptionOperationResult<T>? RefuseBucket<T>(
        SubscriptionDetail subscription,
        int? seatNumber,
        string correlationId)
    {
        if (seatNumber is not { } seat)
        {
            return null;
        }

        if (subscription.Plan.SubscriberScope != SubscriberScope.User)
        {
            return Failure<T>(
                PaymentFailureKind.Validation,
                "subscription_credits_seat_not_applicable",
                "This subscription has no seats. Leave the seat out to use the shared pool.",
                correlationId);
        }

        return seat <= SeatsOf(subscription)
            ? null
            : Failure<T>(
                PaymentFailureKind.NotFound,
                "subscription_credits_seat_not_found",
                $"This subscription has no seat {seat}.",
                correlationId);
    }

    /// <summary>
    /// How many seats a seated subscription bought: the quantity that counts people, or one for a
    /// plan that sells no quantity and so seats one person. Mirrors how seats are handed out.
    /// </summary>
    private static long SeatsOf(SubscriptionDetail subscription)
    {
        var items = subscription.QuantityItems;

        if (items.Count == 1)
        {
            return items[0].Quantity;
        }

        var counting = items.Where(item => item.CountsMembers).ToList();

        return counting.Count == 1 ? counting[0].Quantity : 1;
    }

    private static PlanMeter? FindMeter(SubscriptionDetail subscription, string meterKey) =>
        subscription.Plan.Meters.Find(meter => string.Equals(meter.MeterKey, meterKey, StringComparison.Ordinal));

    /// <summary>
    /// When credits granted now stop being usable: the end of the meter's current usage window for
    /// credits that expire with it, and never for the other two policies.
    /// </summary>
    private static DateTime? ExpiryOf(
        SubscriptionDetail subscription,
        PlanMeter meter,
        MeterResetPolicy policy,
        DateTime nowUtc) =>
        policy == MeterResetPolicy.Periodic &&
        MeterPeriodResolver.TryGetPeriod(subscription, meter, nowUtc, out var period) &&
        period.EndUtc != DateTime.MaxValue
            ? period.EndUtc
            : null;

    // ---- events, audit and mapping --------------------------------------------------------

    private Task AuditAsync(
        SubscriptionContext context,
        SubscriptionDetail subscription,
        string operation,
        string stage,
        string outcome,
        string aggregateId,
        long? amountMinor,
        string? currencyCode,
        string correlationId,
        CancellationToken cancellationToken,
        string? errorCode = null,
        string? failureKind = null,
        string? paymentDetailId = null,
        string? reason = null) =>
        _audit.RecordAsync(
            new SubscriptionAuditEvent
            {
                TenantId = subscription.TenantId,
                OrganizationId = subscription.OrganizationId,
                SubscriptionId = subscription.ItemId,
                AggregateType = "SubscriptionCredit",
                AggregateId = aggregateId,
                OperationId = $"{operation}:{aggregateId}:{stage}",
                CorrelationId = correlationId,
                Operation = operation,
                Stage = stage,
                Outcome = outcome,
                Source = "Api",
                ActorId = context.ActorId,
                UserId = context.UserId,
                PaymentDetailId = paymentDetailId,
                AmountMinor = amountMinor,
                CurrencyCode = currencyCode,
                ErrorCode = errorCode,
                FailureKind = failureKind,
                Reason = reason,
                OccurredAtUtc = Now()
            },
            cancellationToken);

    private static FinancialDocumentLine InvoiceLineFor(
        SubscriptionDetail subscription,
        SubscriptionCreditPurchase purchase)
    {
        var meter = FindMeter(subscription, purchase.MeterKey);

        return new FinancialDocumentLine
        {
            Description = meter is null
                ? $"Credits for {purchase.MeterKey}"
                : CreditLabel(meter),
            Quantity = purchase.Quantity,
            UnitAmountMinor = purchase.UnitAmountMinor,
            AmountMinor = purchase.GrossMinor,
            ItemKey = purchase.MeterKey
        };
    }

    private static string CreditLabel(PlanMeter meter) =>
        string.IsNullOrWhiteSpace(meter.DisplayName) ? $"{meter.MeterKey} credits" : $"{meter.DisplayName} credits";

    /// <summary>
    /// The grant a purchase produces, named from its order token so the same purchase can only ever
    /// produce one.
    /// </summary>
    private static string GrantIdFor(SubscriptionCreditPurchase purchase) =>
        $"purchase:{purchase.OrderId[(purchase.OrderId.LastIndexOf(':') + 1)..]}";

    private static string Fingerprint(CreditPurchaseRequest request) =>
        string.Join(
            '|',
            request.MeterKey,
            Format(request.Quantity),
            request.SeatNumber?.ToString(CultureInfo.InvariantCulture) ?? "pool",
            request.DiscountCode?.Trim().ToLowerInvariant() ?? string.Empty);

    private static string Format(decimal quantity) =>
        quantity.ToString("0.######", CultureInfo.InvariantCulture);

    private static CreditQuoteResponse QuoteResponse(SubscriptionCreditPurchase purchase) => new()
    {
        MeterKey = purchase.MeterKey,
        SeatNumber = purchase.SeatNumber,
        Quantity = purchase.Quantity,
        CurrencyCode = purchase.CurrencyCode,
        UnitAmountMinor = purchase.UnitAmountMinor,
        GrossMinor = purchase.GrossMinor,
        DiscountMinor = purchase.DiscountMinor,
        NetMinor = purchase.NetMinor,
        TaxMinor = purchase.TaxMinor,
        TotalMinor = purchase.TotalMinor,
        TaxRateBasisPoints = purchase.TaxRateBasisPoints,
        TaxMode = purchase.TaxMode?.ToString(),
        DiscountCode = purchase.DiscountCode,
        ExpiryPolicy = purchase.ExpiryPolicy.ToString(),
        ExpiresAtUtc = purchase.ExpiresAtUtc
    };

    private static CreditPurchaseResponse PurchaseResponse(
        SubscriptionCreditPurchase purchase,
        SubscriptionCreditGrant? grant,
        bool replayed) => new()
    {
        PurchaseId = purchase.ItemId,
        Status = purchase.Status.ToString(),
        Quote = QuoteResponse(purchase),
        PaymentId = purchase.PaymentId,
        Grant = grant is null ? null : GrantResponse(grant),
        FailureCode = purchase.FailureCode,
        FailureReason = purchase.FailureReason,
        CreatedAtUtc = purchase.CreatedAtUtc,
        Replayed = replayed
    };

    private static CreditGrantResponse GrantResponse(SubscriptionCreditGrant grant) =>
        GrantResponse(grant, grant.Status);

    private static CreditGrantResponse GrantResponse(SubscriptionCreditGrant grant, CreditGrantStatus status) => new()
    {
        GrantId = grant.ItemId,
        MeterKey = grant.MeterKey,
        SeatNumber = grant.SeatNumber,
        Source = grant.Source.ToString(),
        Quantity = grant.Quantity,
        Remaining = grant.Remaining,
        ExpiryPolicy = grant.ExpiryPolicy.ToString(),
        ExpiresAtUtc = grant.ExpiresAtUtc,
        Status = status.ToString(),
        GrantedAtUtc = grant.GrantedAtUtc,
        GrantedByUserId = grant.GrantedByUserId,
        Reason = grant.Reason,
        PurchaseId = grant.PurchaseId,
        SourceGrantId = grant.SourceGrantId
    };

    private static SubscriptionOperationResult<T> MeterNotFound<T>(string correlationId) =>
        Failure<T>(
            PaymentFailureKind.NotFound,
            "subscription_meter_not_found",
            "This subscription's plan does not define that meter.",
            correlationId);

    private static SubscriptionOperationResult<T> Failure<T>(
        PaymentFailureKind kind,
        string code,
        string message,
        string correlationId) =>
        SubscriptionOperationResult<T>.Failure(kind, code, message, correlationId);

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
}
