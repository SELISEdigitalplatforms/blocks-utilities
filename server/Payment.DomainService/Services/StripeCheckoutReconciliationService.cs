using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Reconciles a Stripe-backed payment whose webhook never arrived, by reading the Checkout
/// Session with its intent's payment method expanded and applying the result exactly as the real
/// webhook would have.
/// </summary>
/// <remarks>
/// Stripe only, deliberately. Adyen's session read carries no payment method detail either, and
/// closing that gap for a second provider is not something this type attempts -- a provider this
/// cannot observe simply reports "not decided", which callers already have to handle for an
/// unreachable Stripe.
/// <para>
/// Not verified against a live Stripe sandbox in this environment: the <c>expand[]</c> query
/// parameters this issues, and the shape of an expanded PaymentIntent/SetupIntent's nested
/// <c>payment_method</c>, follow Stripe's published API reference but have not been exercised
/// against a real account here. Covered by unit tests against mocked HTTP responses instead.
/// </para>
/// </remarks>
public sealed class StripeCheckoutReconciliationService : ISubscriptionPaymentReconciler
{
    private const string ExpandQuery =
        "expand[]=payment_intent.payment_method&expand[]=setup_intent.payment_method";

    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderCache _providerCache;
    private readonly IHttpService _httpService;
    private readonly StripeEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly IPaymentWebhookStateTransitionService _chargeTransitions;
    private readonly IPaymentMethodSetupWebhookStateTransitionService _setupTransitions;
    private readonly TimeProvider _time;
    private readonly ILogger<StripeCheckoutReconciliationService> _logger;

    public StripeCheckoutReconciliationService(
        IPaymentRepository payments,
        IPaymentProviderCache providerCache,
        IHttpService httpService,
        StripeEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        IPaymentWebhookStateTransitionService chargeTransitions,
        IPaymentMethodSetupWebhookStateTransitionService setupTransitions,
        ILogger<StripeCheckoutReconciliationService> logger,
        TimeProvider? time = null)
    {
        _payments = payments;
        _providerCache = providerCache;
        _httpService = httpService;
        _endpointPolicy = endpointPolicy;
        _options = options;
        _chargeTransitions = chargeTransitions;
        _setupTransitions = setupTransitions;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<bool> TryReconcileAsync(
        string tenantId,
        string paymentId,
        CancellationToken cancellationToken)
    {
        var payment = await _payments.GetByIdAsync(tenantId, paymentId, cancellationToken);

        if (payment is null ||
            string.IsNullOrWhiteSpace(payment.SessionId) ||
            !string.Equals(payment.ProviderName, PaymentConstants.StripeProvider, StringComparison.OrdinalIgnoreCase))
        {
            // Not ours to observe. The caller already treats "could not decide" as "keep
            // waiting", which is exactly right for a provider this cannot read.
            return false;
        }

        var provider = await _providerCache.GetAsync(
            payment.TenantId,
            payment.OrganizationId,
            payment.ProviderName,
            () => _payments.GetProviderAsync(
                payment.TenantId,
                payment.OrganizationId,
                payment.ProviderName,
                cancellationToken));

        if (provider is null || !_endpointPolicy.IsAllowed(provider.ApiBaseUrl))
        {
            return false;
        }

        var session = await ReadSessionAsync(provider, payment.SessionId, cancellationToken);

        if (session is null || session.Error is not null)
        {
            return false;
        }

        if (!string.Equals(session.Id, payment.SessionId, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Checkout session reconciliation rejected Reason=session_mismatch PaymentHash={PaymentHash}",
                PaymentLogValue.Hash(payment.ItemId));

            return false;
        }

        var normalizedStatus = new StripeCheckoutStatusMapper().Normalize(
            StripeCheckoutStatusMapper.Compose(session.Status, session.PaymentStatus));

        return normalizedStatus switch
        {
            "completed" => await TryApplyCompletionAsync(payment, session, cancellationToken),
            "expired" => await TryApplyFailureAsync(payment, session, cancellationToken),
            _ => false
        };
    }

    private async Task<StripeCheckoutSessionReconciliation?> ReadSessionAsync(
        PaymentProvider provider,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var url = StripeUrl.Build(
            provider.ApiBaseUrl,
            $"v1/checkout/sessions/{Uri.EscapeDataString(sessionId)}?{ExpandQuery}");

        try
        {
            var (session, error) = await _httpService.SendRequest<StripeCheckoutSessionReconciliation>(
                HttpMethod.Get,
                url,
                null!,
                "application/x-www-form-urlencoded",
                StripeRequestHeaders.Read(provider),
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

            if (session is null && !string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning(
                    "Checkout session reconciliation read returned no usable response Provider={Provider}",
                    PaymentLogValue.Label(provider.ProviderName));
            }

            return session;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Checkout session reconciliation read failed Provider={Provider} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return null;
        }
    }

    /// <summary>
    /// The session says the shopper finished. Applied through the same path a real webhook uses,
    /// so a later, genuinely late webhook for the same event is a harmless no-op rather than a
    /// second application.
    /// </summary>
    private async Task<bool> TryApplyCompletionAsync(
        PaymentDetail payment,
        StripeCheckoutSessionReconciliation session,
        CancellationToken cancellationToken)
    {
        var isSetup = string.Equals(
            payment.PaymentFlow, PaymentFlows.PaymentMethodSetup, StringComparison.Ordinal);
        var intent = isSetup ? session.SetupIntent : session.PaymentIntent;
        var pspReference = intent?.Id;

        if (string.IsNullOrWhiteSpace(pspReference))
        {
            // A completed session with no intent to point at is not something this can safely
            // apply -- there is nothing to record as the confirming reference. Left undecided
            // rather than guessed at.
            _logger.LogWarning(
                "Checkout session reconciliation found no intent on a completed session " +
                "PaymentHash={PaymentHash}",
                PaymentLogValue.Hash(payment.ItemId));

            return false;
        }

        long? amountMinorUnits = null;
        string? currencyCode = null;

        if (!isSetup)
        {
            if (session.AmountTotal is null || session.Currency is null)
            {
                // The charge path checks the event's amount against the payment before applying
                // anything, and there is nothing to check it against here.
                _logger.LogWarning(
                    "Checkout session reconciliation found no amount on a completed charge " +
                    "session PaymentHash={PaymentHash}",
                    PaymentLogValue.Hash(payment.ItemId));

                return false;
            }

            amountMinorUnits = session.AmountTotal;
            currencyCode = session.Currency.ToUpperInvariant();
        }

        var method = intent?.PaymentMethod;

        var payload = new PaymentWebhookPayload
        {
            PaymentDetailId = payment.ItemId,
            ProviderName = PaymentConstants.StripeProvider,
            PspReference = pspReference,
            Success = true,
            AmountMinorUnits = amountMinorUnits,
            CurrencyCode = currencyCode,
            // Our own stored value, not an echoed one: this call is authenticated to Stripe with
            // our own API key about a payment this service already resolved by id, unlike an
            // inbound webhook, which must prove it belongs to what it claims. There is nothing
            // here for that value to be validated against.
            ShopperReference = payment.ShopperReference,
            ProviderPayerReference = intent?.Customer,
            StoredPaymentMethodToken = method?.Id,
            PaymentMethodType = method?.Type ?? "card",
            Brand = method?.Card?.Brand,
            LastFour = method?.Card?.Last4
        };

        return await ApplyAsync(payment, isSetup, payload, cancellationToken);
    }

    /// <summary>
    /// The session expired without ever being used. Applied as a decline through the same path a
    /// real webhook would, so the sweep's own <c>TerminalFailureStatuses</c> check recognizes it.
    /// </summary>
    private async Task<bool> TryApplyFailureAsync(
        PaymentDetail payment,
        StripeCheckoutSessionReconciliation session,
        CancellationToken cancellationToken)
    {
        var isSetup = string.Equals(
            payment.PaymentFlow, PaymentFlows.PaymentMethodSetup, StringComparison.Ordinal);
        var intent = isSetup ? session.SetupIntent : session.PaymentIntent;

        // A session that expired before Stripe ever created an intent still has to be recorded
        // as a decided failure, so this is never left null -- but it must never collide with a
        // real event's own reference, which always names an actual intent.
        var pspReference = intent?.Id is { Length: > 0 } id
            ? id
            : $"reconciled-expiry:{payment.ItemId}";

        var payload = new PaymentWebhookPayload
        {
            PaymentDetailId = payment.ItemId,
            ProviderName = PaymentConstants.StripeProvider,
            PspReference = pspReference,
            Success = false,
            AmountMinorUnits = isSetup ? null : session.AmountTotal,
            CurrencyCode = isSetup ? null : session.Currency?.ToUpperInvariant()
        };

        return await ApplyAsync(payment, isSetup, payload, cancellationToken);
    }

    private async Task<bool> ApplyAsync(
        PaymentDetail payment,
        bool isSetup,
        PaymentWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        var webhook = new PaymentWebhookInbox
        {
            WebhookId = Guid.NewGuid().ToString("N"),
            TenantId = payment.TenantId,
            ProviderName = PaymentConstants.StripeProvider,
            WebhookType = "reconciliation",
            EventCode = "checkout_session_reconciliation",
            Intent = isSetup ? WebhookIntent.PaymentMethodSetup : WebhookIntent.Authorization,
            EventDateUtc = _time.GetUtcNow().UtcDateTime,
            PspReference = payload.PspReference,
            NormalizedPayload = payload,
            CorrelationId = payment.CorrelationId
        };

        try
        {
            if (isSetup)
            {
                await _setupTransitions.ApplyAsync(webhook, cancellationToken);
            }
            else
            {
                await _chargeTransitions.ApplyAsync(webhook, cancellationToken);
            }

            return true;
        }
        catch (InvalidOperationException exception)
        {
            // A malformed synthetic event is a bug in this reconciler, not evidence about the
            // payment. Reported and left undecided rather than crashing the activation sweep that
            // called this.
            _logger.LogError(
                "Checkout session reconciliation could not be applied PaymentHash={PaymentHash} " +
                "ExceptionMessage={ExceptionMessage}",
                PaymentLogValue.Hash(payment.ItemId),
                exception.Message);

            return false;
        }
    }
}
