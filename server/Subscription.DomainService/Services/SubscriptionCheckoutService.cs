using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Subscribing, end to end: create the record, then take the money if there is any to take.
/// </summary>
/// <remarks>
/// Orchestration only. Building the subscription belongs to the creation service, computing the
/// amount to the calculator, and moving money to the payment module — this decides the order
/// and what to do when a step declines.
/// </remarks>
public sealed class SubscriptionCheckoutService : ISubscriptionCheckoutService
{
    private readonly ISubscriptionCreationService _creation;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionPaymentLinkRepository _links;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly ISubscriptionResponseMapper _mapper;
    private readonly IPaymentService _payments;
    private readonly IPaymentMethodSetupService _paymentMethodSetups;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ICurrencyMinorUnitResolver _currency;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly ILogger<SubscriptionCheckoutService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionCheckoutService(
        ISubscriptionCreationService creation,
        ISubscriptionRepository subscriptions,
        ISubscriptionPaymentLinkRepository links,
        ISubscriptionContextResolver contextResolver,
        ISubscriptionOutboxEventFactory events,
        ISubscriptionResponseMapper mapper,
        IPaymentService payments,
        IPaymentMethodSetupService paymentMethodSetups,
        IPaymentRepository paymentRepository,
        ICurrencyMinorUnitResolver currency,
        IBillingAccountRepository billingAccounts,
        ILogger<SubscriptionCheckoutService> logger,
        ISubscriptionFinancialDocumentAnnouncer? documents = null,
        TimeProvider? time = null)
    {
        _creation = creation;
        _subscriptions = subscriptions;
        _links = links;
        _contextResolver = contextResolver;
        _events = events;
        _mapper = mapper;
        _payments = payments;
        _paymentMethodSetups = paymentMethodSetups;
        _paymentRepository = paymentRepository;
        _currency = currency;
        _billingAccounts = billingAccounts;
        _logger = logger;
        _documents = documents;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Optional so existing callers and tests compile unchanged. A card-free trial that starts
    /// without announcing its document is one the repair sweep has to find, not one that failed.
    /// </summary>
    private readonly ISubscriptionFinancialDocumentAnnouncer? _documents;

    public async Task<SubscriptionOperationResult<SubscriptionResponse>> SubscribeAsync(
        CreateSubscriptionRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<SubscriptionResponse>(correlationId);
        }

        var context = resolution.Context!;

        var created = await _creation.CreateAsync(
            request,
            context,
            correlationId,
            cancellationToken);

        if (!created.IsSuccess)
        {
            if (string.Equals(
                    created.ErrorCode,
                    "subscription_already_active",
                    StringComparison.Ordinal))
            {
                var resumed = await TryResumeIncompleteCheckoutAsync(
                    request,
                    context,
                    correlationId,
                    cancellationToken);

                if (resumed is not null)
                {
                    return resumed;
                }
            }

            return created.ToFailure<SubscriptionResponse>();
        }

        var subscription = created.Value!;

        // The figure fixed when the subscription was built, so the charge raised here is the one
        // the customer was quoted — and the same expression the purchase preview reports, so the
        // two cannot disagree.
        var amountMinor = SubscriptionAmountCalculator.InitialChargeAmountMinor(subscription);

        if (RequiresPayment(amountMinor))
        {
            return await ChargeAsync(subscription, amountMinor, correlationId, cancellationToken);
        }

        // Nothing is payable today. Whether that means the subscription starts now depends on
        // what the plan asked for: a card can be a condition of activation without being a
        // charge, and the two questions were conflated for as long as the only way to hold a
        // card was to take money with it.
        return SubscriptionAmountCalculator.RequiresCardSetup(subscription)
            ? await StartCardSetupAsync(subscription, correlationId, cancellationToken)
            : await StartWithoutPaymentAsync(
                subscription,
                context,
                correlationId,
                cancellationToken);
    }

    private async Task<SubscriptionOperationResult<SubscriptionResponse>?> TryResumeIncompleteCheckoutAsync(
        CreateSubscriptionRequest request,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptions.GetIncompleteAsync(
            context.TenantId,
            context.OrganizationId,
            cancellationToken);

        if (subscription is null)
        {
            // A live subscription caused the unique-index collision, or it moved state between
            // the insert and this read. Preserve the ordinary already-active response.
            return null;
        }

        var link = await _links.FindBySubscriptionAsync(
            context.TenantId,
            subscription.ItemId,
            cancellationToken);

        var checkoutUrl = await ResolveUsableCheckoutUrlAsync(
            context.TenantId,
            link,
            cancellationToken);

        if (!MatchesPendingTerms(request, subscription))
        {
            return PendingCheckoutConflict(subscription, checkoutUrl, correlationId);
        }

        if (string.IsNullOrWhiteSpace(checkoutUrl))
        {
            // A card-collection session that has expired is not a dead end. Nothing was paid, so
            // there is no money to reconcile and no reason to make someone cancel and start
            // again — but the expired session cannot be reopened, so the retry has to be a new
            // one under a new key.
            //
            // A *charge* is left alone. Raising a second one is how the same money gets taken
            // twice, and the existing conflict tells the caller to finish or cancel the first.
            return link is { Purpose: SubscriptionPaymentPurpose.PaymentMethodSetup }
                ? await RetryCardSetupAsync(subscription, link, correlationId, cancellationToken)
                : PendingCheckoutConflict(subscription, null, correlationId);
        }

        _logger.LogInformation(
            "Existing subscription checkout resumed TenantHash={TenantHash} " +
            "OrganizationHash={OrganizationHash} SubscriptionHash={SubscriptionHash} " +
            "CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(context.OrganizationId),
            PaymentLogValue.Hash(subscription.ItemId),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            await _mapper.ToResponseAsync(
                _billingAccounts,
                subscription,
                checkoutUrl,
                link is { Purpose: SubscriptionPaymentPurpose.PaymentMethodSetup }
                    ? PendingSetup("Pending", checkoutUrl)
                    : null,
                cancellationToken),
            correlationId);
    }

    private async Task<string?> GetPendingCheckoutUrlAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken) =>
        await ResolveUsableCheckoutUrlAsync(
            tenantId,
            await _links.FindBySubscriptionAsync(tenantId, subscriptionId, cancellationToken),
            cancellationToken);

    private async Task<PendingCheckoutResponse?> GetPendingSetupAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        var link = await _links.FindBySubscriptionAsync(
            tenantId,
            subscriptionId,
            cancellationToken);

        if (link is not { Purpose: SubscriptionPaymentPurpose.PaymentMethodSetup })
        {
            return null;
        }

        var payment = await _paymentRepository.GetByIdAsync(
            tenantId,
            link.PaymentDetailId,
            cancellationToken);

        if (payment is null)
        {
            return PendingSetup("Failed", null, "payment_method_setup_not_found");
        }

        var expired = payment.ExpirationDate != default &&
                      payment.ExpirationDate <= DateTime.UtcNow;
        if (expired)
        {
            return PendingSetup("Expired", null, "payment_method_setup_expired");
        }

        if (payment.PaymentStatus is PaymentStatuses.Refused or
            PaymentStatuses.Cancelled or
            PaymentStatuses.MakePaymentFailed)
        {
            return PendingSetup(
                "Failed",
                null,
                "payment_method_setup_failed");
        }

        var url = link.State == SubscriptionPaymentLinkState.Pending
            ? await ResolveUsableCheckoutUrlAsync(tenantId, link, cancellationToken)
            : null;

        return link.State == SubscriptionPaymentLinkState.Pending && url is not null
            ? PendingSetup("Pending", url)
            : PendingSetup("Expired", null, "payment_method_setup_expired");
    }

    private static PendingCheckoutResponse PendingSetup(
        string state,
        string? checkoutUrl,
        string? errorCode = null) => new()
    {
        Purpose = nameof(SubscriptionPaymentPurpose.PaymentMethodSetup),
        State = state,
        ErrorCode = errorCode,
        CheckoutUrl = checkoutUrl
    };

    /// <summary>
    /// The hosted page this link still leads to, or null when there is nothing left to return to.
    /// </summary>
    private async Task<string?> ResolveUsableCheckoutUrlAsync(
        string tenantId,
        SubscriptionPaymentLink? link,
        CancellationToken cancellationToken)
    {
        if (link is null || link.State != SubscriptionPaymentLinkState.Pending)
        {
            return null;
        }

        // The link is already tenant- and organization-scoped. Read the linked payment directly:
        // PaymentService's caller-facing lookup scopes by the merchant OrganizationId, while a
        // subscription payment belongs to the subscriber through CustomerOrganizationId.
        var payment = await _paymentRepository.GetByIdAsync(
            tenantId,
            link.PaymentDetailId,
            cancellationToken);

        return payment is null ||
               payment.PaymentStatus is PaymentStatuses.Refused or
                   PaymentStatuses.Cancelled or
                   PaymentStatuses.MakePaymentFailed ||
               string.IsNullOrWhiteSpace(payment.RedirectUrl) ||
               payment.ExpirationDate != default && payment.ExpirationDate <= DateTime.UtcNow
            ? null
            : payment.RedirectUrl;
    }

    private static bool MatchesPendingTerms(
        CreateSubscriptionRequest request,
        SubscriptionDetail subscription)
    {
        if (!string.Equals(request.PlanCode, subscription.Plan.Code, StringComparison.Ordinal) ||
            !string.Equals(request.PriceId, subscription.Price.PriceId, StringComparison.Ordinal) ||
            !string.Equals(
                request.DiscountCode?.Trim(),
                subscription.Discount?.Code,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                request.TimeZoneId,
                subscription.FeeSchedule.TimeZoneId,
                StringComparison.Ordinal))
        {
            return false;
        }

        return request.Quantities.Count == 0 || request.Quantities.All(requested =>
            subscription.QuantityItems.Any(existing =>
                string.Equals(existing.ItemKey, requested.ItemKey, StringComparison.Ordinal) &&
                existing.Quantity == requested.Quantity));
    }

    private static SubscriptionOperationResult<SubscriptionResponse> PendingCheckoutConflict(
        SubscriptionDetail subscription,
        string? checkoutUrl,
        string correlationId)
    {
        var details = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["subscriptionId"] = [subscription.ItemId]
        };

        if (!string.IsNullOrWhiteSpace(checkoutUrl))
        {
            details["checkoutUrl"] = [checkoutUrl];
        }

        return SubscriptionOperationResult<SubscriptionResponse>.Failure(
            PaymentFailureKind.Conflict,
            "subscription_checkout_pending",
            "This organization already has an unpaid checkout. Continue it or cancel it before choosing different terms.",
            correlationId,
            details);
    }

    public async Task<SubscriptionOperationResult<SubscriptionResponse>> GetCurrentAsync(
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            organizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<SubscriptionResponse>(correlationId);
        }

        var context = resolution.Context!;

        var subscription = await _subscriptions.GetLiveAsync(
            context.TenantId,
            context.OrganizationId,
            _time.GetUtcNow().UtcDateTime,
            cancellationToken);

        if (subscription is not null)
        {
            return SubscriptionOperationResult<SubscriptionResponse>.Success(
                await _mapper.ToResponseAsync(
                    _billingAccounts, subscription, null, null, cancellationToken),
                correlationId);
        }

        subscription = await _subscriptions.GetIncompleteAsync(
            context.TenantId,
            context.OrganizationId,
            cancellationToken);

        if (subscription is not null)
        {
            var pendingSetup = await GetPendingSetupAsync(
                context.TenantId,
                subscription.ItemId,
                cancellationToken);

            var checkoutUrl = pendingSetup is not null
                ? pendingSetup.CheckoutUrl
                : await GetPendingCheckoutUrlAsync(
                    context.TenantId,
                    subscription.ItemId,
                    cancellationToken);

            return SubscriptionOperationResult<SubscriptionResponse>.Success(
                await _mapper.ToResponseAsync(
                    _billingAccounts, subscription, checkoutUrl, pendingSetup, cancellationToken),
                correlationId);
        }

        // Unpaid grants nothing, so GetLiveAsync above never finds it — but it is a subscription
        // the caller still has, and one they can still act on. Without this it read exactly like no
        // subscription at all, which left no way for a client to offer the one thing that fixes it:
        // saving a card through POST .../payment-method/setup.
        subscription = await _subscriptions.GetUnpaidAsync(
            context.TenantId,
            context.OrganizationId,
            cancellationToken);

        if (subscription is not null)
        {
            // Computed rather than assumed false. Unpaid ordinarily has no card by definition, but
            // a card adopted moments ago by RecoverAsync's own transition can be visible here
            // before that transition's status write has -- reading it for real means this can
            // never claim "no card" about a subscription that already has one.
            return SubscriptionOperationResult<SubscriptionResponse>.Success(
                await _mapper.ToResponseAsync(
                    _billingAccounts, subscription, null, null, cancellationToken),
                correlationId);
        }

        // No subscription is an answer, not a failure. This used to be a 404, which says the
        // endpoint is not there: a client cannot tell that from a bad route, a revoked path or a
        // typo, so every caller had to special-case one status code to read an ordinary "not yet".
        // The other not-found refusals in this module stay as they are - asking to cancel or reprice
        // a subscription that does not exist really is a request about something absent.
        return SubscriptionOperationResult<SubscriptionResponse>.Empty(correlationId);
    }

    /// <summary>
    /// Whether money has to move before this subscription grants anything.
    /// </summary>
    /// <remarks>
    /// A card-free trial and a fully discounted first period both come to nothing payable, and
    /// a zero-amount charge is not something the money path accepts — the currency resolver
    /// refuses anything at or below zero. So these start directly rather than being sent to a
    /// checkout that would decline them.
    /// <para>
    /// <paramref name="amountMinor"/> is already trial-aware — see
    /// <see cref="SubscriptionAmountCalculator.InitialChargeAmountMinor"/> — so the only question
    /// left here is whether it came to anything.
    /// </para>
    /// </remarks>
    private static bool RequiresPayment(long amountMinor) => amountMinor > 0;

    /// <summary>
    /// The address Stripe should prefill, read from the billing account rather than asked of the
    /// caller.
    /// </summary>
    /// <remarks>
    /// Set once, at signup, from the billing profile's own contact -- see
    /// SubscriptionCreationService's BillingEmail assignment -- so by the time a subscription can
    /// be charged or asked to collect a card, this is already the address on file. Read here
    /// rather than threaded through from the caller because ChargeAsync and StartCardSetupAsync
    /// are reached from more than one entry point, some of which have never had a reason to know
    /// the billing account until now.
    /// </remarks>
    private async Task<string?> BillingEmailAsync(
        SubscriptionDetail subscription,
        CancellationToken cancellationToken)
    {
        var account = await _billingAccounts.GetAsync(
            subscription.TenantId, subscription.BillingAccountId, cancellationToken);

        return account?.BillingEmail is { Length: > 0 } email ? email : null;
    }

    /// <summary>
    /// The provider (and merchant organization scope) this subscription was pinned to at
    /// creation, read from its billing account rather than re-resolved from the merchant profile.
    /// </summary>
    /// <remarks>
    /// A missing billing account, or one recorded with no provider, used to fall back to Stripe --
    /// which meant an Adyen subscription whose billing account went missing or was corrupted
    /// would silently route its next charge through Stripe instead of failing. That is exactly
    /// the fail-open bug this PR's own frozen-provider guarantee exists to prevent, so this now
    /// fails closed: a subscription that cannot say what it was actually pinned to cannot be
    /// charged through a provider it was never pinned to either.
    /// </remarks>
    private async Task<BillingAccountProviderResolution> ResolveBillingAccountProviderAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var account = await _billingAccounts.GetAsync(
            subscription.TenantId, subscription.BillingAccountId, cancellationToken);

        if (account?.ProviderName is { Length: > 0 } providerName)
        {
            return new BillingAccountProviderResolution(account, providerName, null);
        }

        _logger.LogError(
            "Subscription billing account has no usable payment provider on file -- refusing to " +
            "fall back to a different provider TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} " +
            "BillingAccountHash={BillingAccountHash} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Hash(subscription.BillingAccountId),
            correlationId);

        return new BillingAccountProviderResolution(
            account,
            null,
            Failure(
                PaymentFailureKind.Unavailable,
                "subscription_billing_account_provider_unavailable",
                "This subscription's billing account has no payment provider on file. It cannot " +
                    "be charged until support restores its provider configuration.",
                correlationId));
    }

    /// <summary>
    /// What resolving a subscription's frozen provider found: either an account and the provider
    /// to charge through, or the failure to return instead of guessing at one.
    /// </summary>
    private readonly record struct BillingAccountProviderResolution(
        BillingAccount? Account,
        string? ProviderName,
        SubscriptionOperationResult<SubscriptionResponse>? Failure);

    /// <inheritdoc />
    public async Task<SubscriptionOperationResult<SubscriptionResponse>> StartPaymentMethodSetupAsync(
        string subscriptionId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            organizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<SubscriptionResponse>(correlationId);
        }

        var context = resolution.Context!;

        var subscription = await _subscriptions.GetByIdAsync(
            context.TenantId,
            subscriptionId,
            cancellationToken);

        // Scope checked here rather than left to the query, because GetByIdAsync is tenant-scoped
        // and not organization-scoped: without this, naming another organization's subscription id
        // would open a card session against it.
        if (subscription is null ||
            !string.Equals(subscription.OrganizationId, context.OrganizationId, StringComparison.Ordinal))
        {
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "No subscription was found to add a payment method to.",
                correlationId);
        }

        if (subscription.Status is SubscriptionStatus.Canceled or SubscriptionStatus.IncompleteExpired)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_not_collectable",
                "This subscription has ended, so there is nothing for a payment method to pay.",
                correlationId);
        }

        // Unpaid is deliberately allowed through to the same session a trial uses to add a card.
        // The confirmation this session produces goes through SubscriptionActivationProcessor,
        // which charges the overdue period the moment the card is adopted and only then restores
        // access -- see RecoverAsync and its own guard against a decline granting access through
        // PastDue.

        var account = await _billingAccounts.GetAsync(
            context.TenantId,
            subscription.BillingAccountId,
            cancellationToken);

        if (account?.DefaultPaymentMethodId is { Length: > 0 })
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "payment_method_already_stored",
                "This subscription already has a payment method.",
                correlationId);
        }

        // A session already open is returned rather than replaced. Two live sessions against one
        // subscription is how a subscriber ends up with the card they did not expect on file, and
        // the activation sweep only ever settles one of them.
        var existing = await _links.FindBySubscriptionAsync(
            context.TenantId,
            subscription.ItemId,
            cancellationToken);

        if (existing is { Purpose: SubscriptionPaymentPurpose.PaymentMethodSetup,
                          State: SubscriptionPaymentLinkState.Pending })
        {
            var url = await ResolveUsableCheckoutUrlAsync(context.TenantId, existing, cancellationToken);

            if (url is { Length: > 0 })
            {
                return SubscriptionOperationResult<SubscriptionResponse>.Success(
                    _mapper.ToResponse(
                        subscription,
                        url,
                        PendingSetup("Pending", url),
                        providerName: account?.ProviderName),
                    correlationId);
            }

            // The session expired without anybody finishing it. Bumping the attempt is what makes
            // the next one distinct, and it is a compare-and-set, so two tabs produce one session.
            return await RetryCardSetupAsync(subscription, existing, correlationId, cancellationToken);
        }

        return await StartCardSetupAsync(subscription, correlationId, cancellationToken);
    }

    /// <summary>
    /// Opens a hosted session that stores a card and charges nothing.
    /// </summary>
    /// <remarks>
    /// The subscription stays <see cref="SubscriptionStatus.Incomplete"/> and grants nothing
    /// until the provider confirms the card was stored — the same rule a paid signup follows, and
    /// for the same reason: a browser that came back is not evidence, and the webhook is.
    /// </remarks>
    private async Task<SubscriptionOperationResult<SubscriptionResponse>> StartCardSetupAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // The billing account's own frozen provider, never re-resolved from the merchant profile:
        // this subscription was pinned to a provider at creation, and a later change to the
        // tenant's selection must never move where its card is collected.
        var resolution = await ResolveBillingAccountProviderAsync(
            subscription, correlationId, cancellationToken);

        if (resolution.Failure is { } billingAccountFailure)
        {
            return billingAccountFailure;
        }

        var account = resolution.Account!;
        var providerName = resolution.ProviderName!;

        // The organization scope frozen alongside the provider at creation -- not necessarily
        // the subscriber's own -- so the session opens against the exact merchant configuration
        // that was validated ready, never one resolved independently from the caller's own
        // ambient context. See BillingAccount.ProviderOrganizationId and
        // ISubscriptionPaymentProviderReadinessService.
        var providerOrganizationId = account.ProviderOrganizationId ?? subscription.OrganizationId;

        var setup = await _paymentMethodSetups.CreateSetupAsync(
            new CreatePaymentMethodSetupRequest
            {
                ProviderName = providerName,
                CurrencyCode = subscription.CurrencyCode,
                OrderId = subscription.OrderId,
                Description = $"{subscription.Plan.DisplayName} subscription",
                CustomerOrganizationId = subscription.OrganizationId,
                OrganizationId = providerOrganizationId,
                // Same reason ChargeAsync carries it: prefilled once here rather than typed twice
                // -- once on the billing profile, again on Stripe's own page.
                CustomerEmail = await BillingEmailAsync(subscription, cancellationToken)
            },
            SubscriptionConstants.PaymentMethodSetupKeyFor(
                subscription.ItemId,
                subscription.PaymentMethodSetupAttempt),
            correlationId,
            cancellationToken);

        if (!setup.IsSuccess || setup.Payment is null)
        {
            _logger.LogWarning(
                "Subscription card setup failed TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} Reason={Reason} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(setup.ErrorCode),
                correlationId);

            // Incomplete, granting nothing, and recoverable. No money moved, so there is nothing
            // to unwind and nothing that stops another attempt.
            return Failure(
                setup.FailureKind,
                setup.ErrorCode,
                setup.ErrorMessage,
                correlationId);
        }

        // The session must actually have opened against the scope this subscription was pinned
        // to. Without this check, an authorization gap anywhere in the payment module's own
        // organization resolution (see IPaymentOrganizationResolver) would silently collect a
        // card under a different merchant's configuration than the one that passed readiness --
        // exactly the class of bug this PR exists to close. Never trusted implicitly, even
        // though the request just asked for this scope explicitly.
        if (!string.Equals(setup.Payment.OrganizationId, providerOrganizationId, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Subscription card setup resolved a different provider organization than the " +
                "billing account was pinned to -- refusing to adopt it TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} PaymentHash={PaymentHash} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Hash(setup.Payment.PaymentDetailId),
                correlationId);

            return Failure(
                PaymentFailureKind.Unavailable,
                "subscription_payment_provider_scope_mismatch",
                "The payment provider could not be reached under the merchant configuration " +
                    "this subscription was pinned to.",
                correlationId);
        }

        await _links.TryCreateAsync(
            new SubscriptionPaymentLink
            {
                TenantId = subscription.TenantId,
                OrganizationId = subscription.OrganizationId,
                SubscriptionId = subscription.ItemId,
                PaymentDetailId = setup.Payment.PaymentDetailId,
                OrderId = subscription.OrderId,
                Purpose = SubscriptionPaymentPurpose.PaymentMethodSetup,
                State = SubscriptionPaymentLinkState.Pending,
                CorrelationId = correlationId
            },
            cancellationToken);

        _logger.LogInformation(
            "Subscription card setup started TenantHash={TenantHash} " +
            "SubscriptionHash={SubscriptionHash} PaymentHash={PaymentHash} Attempt={Attempt} " +
            "CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Hash(setup.Payment.PaymentDetailId),
            subscription.PaymentMethodSetupAttempt,
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(
                subscription,
                setup.Payment.RedirectUrl,
                PendingSetup("Pending", setup.Payment.RedirectUrl),
                providerName: providerName),
            correlationId);
    }

    /// <summary>
    /// Opens a fresh card-collection session after the last one expired.
    /// </summary>
    /// <remarks>
    /// The attempt counter is bumped first, and that write is the gate: it is a compare-and-set on
    /// the number itself, so two tabs retrying at once produce one new session and the loser is
    /// told the setup is already in progress rather than opening a third.
    /// </remarks>
    private async Task<SubscriptionOperationResult<SubscriptionResponse>> RetryCardSetupAsync(
        SubscriptionDetail subscription,
        SubscriptionPaymentLink link,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!await _subscriptions.TryBumpPaymentMethodSetupAttemptAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.PaymentMethodSetupAttempt,
                cancellationToken))
        {
            return PendingCheckoutConflict(subscription, null, correlationId);
        }

        // Nobody is going to finish the expired one, and a pending link is what the activation
        // sweep keeps coming back to. Settled after the bump: an abandoned link with no
        // replacement is recoverable, a second live session under a stale number is not.
        await _links.TrySettleAsync(
            subscription.TenantId,
            link.ItemId,
            SubscriptionPaymentLinkState.Abandoned,
            cancellationToken);

        subscription.PaymentMethodSetupAttempt++;

        return await StartCardSetupAsync(subscription, correlationId, cancellationToken);
    }

    private async Task<SubscriptionOperationResult<SubscriptionResponse>> ChargeAsync(
        SubscriptionDetail subscription,
        long amountMinor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!_currency.TryConvertBack(
                amountMinor,
                subscription.CurrencyCode,
                out var amount))
        {
            return Failure(
                PaymentFailureKind.Unavailable,
                "subscription_currency_unsupported",
                "This currency is not configured for payments.",
                correlationId);
        }

        // Same reason as StartCardSetupAsync: the billing account's frozen provider, not
        // whatever the merchant profile currently says.
        var resolution = await ResolveBillingAccountProviderAsync(
            subscription, correlationId, cancellationToken);

        if (resolution.Failure is { } billingAccountFailure)
        {
            return billingAccountFailure;
        }

        var account = resolution.Account!;
        var providerName = resolution.ProviderName!;

        // Same reason as StartCardSetupAsync: the scope frozen alongside the provider, not the
        // caller's own ambient organization.
        var providerOrganizationId = account.ProviderOrganizationId ?? subscription.OrganizationId;

        var payment = await _payments.MakePaymentAsync(
            new MakePaymentRequest
            {
                ProviderName = providerName,
                Amount = amount,
                CurrencyCode = subscription.CurrencyCode,
                OrderId = subscription.OrderId,
                Description = $"{subscription.Plan.DisplayName} subscription",
                CustomerOrganizationId = subscription.OrganizationId,
                OrganizationId = providerOrganizationId,
                // Stripe uses this to prefill the checkout page's email field. Without it the
                // subscriber -- whose address the billing profile already collected a step
                // earlier -- has to type it again on the provider's own page.
                CustomerEmail = await BillingEmailAsync(subscription, cancellationToken),
                // The renewal in a month charges this card with nobody present, which the
                // provider only permits if the mandate was established when it was saved.
                SavePaymentMethod = true,
                // The token this charge saves is for scheduled, merchant-initiated renewals --
                // Adyen's "Subscription" recurring model, not "CardOnFile" (shopper-initiated,
                // on-demand top-ups). AdyenInitiationRequestFactory honors this when present and
                // otherwise keeps defaulting to CardOnFile for any other caller of that factory.
                // Not verified against a live Adyen sandbox in this environment -- see the
                // factory's own remarks and the PR description's "not verified live" callout.
                RecurringModel = PaymentConstants.SubscriptionRecurringModel
            },
            // Derived from the subscription, so a retried request finds the same payment
            // instead of raising a second one — and so the recovery sweep can find it too.
            SubscriptionConstants.InitialChargeKeyFor(subscription.ItemId),
            correlationId,
            cancellationToken);

        if (!payment.IsSuccess || payment.Payment is null)
        {
            _logger.LogWarning(
                "Subscription initial charge failed TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} Reason={Reason} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(payment.ErrorCode),
                correlationId);

            // The subscription stays incomplete and grants nothing. The customer can try
            // again from a clean state, and the recovery sweep tidies it if they do not.
            return Failure(
                payment.FailureKind,
                payment.ErrorCode,
                payment.ErrorMessage,
                correlationId);
        }

        // Same reason as StartCardSetupAsync: the charge must actually have been resolved
        // against the scope this subscription was pinned to, not merely asked for it.
        if (!string.Equals(payment.Payment.OrganizationId, providerOrganizationId, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Subscription initial charge resolved a different provider organization than " +
                "the billing account was pinned to -- refusing to adopt it TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} PaymentHash={PaymentHash} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Hash(payment.Payment.PaymentDetailId),
                correlationId);

            return Failure(
                PaymentFailureKind.Unavailable,
                "subscription_payment_provider_scope_mismatch",
                "The payment provider could not be reached under the merchant configuration " +
                    "this subscription was pinned to.",
                correlationId);
        }

        await _links.TryCreateAsync(
            new SubscriptionPaymentLink
            {
                TenantId = subscription.TenantId,
                OrganizationId = subscription.OrganizationId,
                SubscriptionId = subscription.ItemId,
                PaymentDetailId = payment.Payment.PaymentDetailId,
                OrderId = subscription.OrderId,
                Purpose = SubscriptionPaymentPurpose.InitialCharge,
                State = SubscriptionPaymentLinkState.Pending,
                CorrelationId = correlationId
            },
            cancellationToken);

        _logger.LogInformation(
            "Subscription checkout started TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} " +
            "PaymentHash={PaymentHash} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Hash(payment.Payment.PaymentDetailId),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(
                subscription, payment.Payment.RedirectUrl, providerName: providerName),
            correlationId);
    }

    private async Task<SubscriptionOperationResult<SubscriptionResponse>> StartWithoutPaymentAsync(
        SubscriptionDetail subscription,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var target = subscription.Trial is null
            ? SubscriptionStatus.Active
            : SubscriptionStatus.Trialing;

        var eventType = target == SubscriptionStatus.Trialing
            ? SubscriptionConstants.SubscriptionTrialStarted
            : SubscriptionConstants.SubscriptionActivated;

        var applied = await _subscriptions.TryTransitionAsync(
            subscription.TenantId,
            subscription.ItemId,
            new SubscriptionTransition(SubscriptionStatus.Incomplete, target)
            {
                ActivatedAtUtc = DateTime.UtcNow,
                Event = _events.Create(subscription, eventType, correlationId)
            },
            cancellationToken);

        if (!applied)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_transition_conflict",
                "The subscription changed while it was being started.",
                correlationId);
        }

        subscription.Status = target;

        if (target == SubscriptionStatus.Trialing && _documents is not null)
        {
            // The card-free trial. No money moved and there is nothing to invoice, but the
            // subscriber has entitlement they were granted on stated terms, and that is what the
            // zero-total trial invoice records.
            await _documents.AnnounceTrialAsync(
                subscription,
                correlationId,
                cancellationToken,
                SubscriptionDocumentSourceFactory.ActorOf(
                    context.UserId,
                    context.UserName,
                    context.UserEmail));
        }

        _logger.LogInformation(
            "Subscription started without payment TenantHash={TenantHash} " +
            "SubscriptionHash={SubscriptionHash} Status={Status} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(target.ToString()),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            await _mapper.ToResponseAsync(
                _billingAccounts, subscription, null, null, cancellationToken),
            correlationId);
    }

    private static SubscriptionOperationResult<SubscriptionResponse> Failure(
        PaymentFailureKind kind,
        string errorCode,
        string errorMessage,
        string correlationId) =>
        SubscriptionOperationResult<SubscriptionResponse>.Failure(
            kind,
            errorCode,
            errorMessage,
            correlationId);
}
