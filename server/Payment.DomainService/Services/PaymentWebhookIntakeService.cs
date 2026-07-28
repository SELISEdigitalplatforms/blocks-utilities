using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.Webhooks;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Admits inbound webhooks. Parses with the provider's normalizer, proves each event is
/// authentic and really owns the payment it names, then durably records it for the worker.
/// Never mutates payment state itself.
/// </summary>
public sealed class PaymentWebhookIntakeService : IPaymentWebhookIntakeService
{
    private readonly IPaymentRepository _payments;
    private readonly IPaymentRefundRepository _refunds;
    private readonly IPaymentCaptureRepository _captures;
    private readonly IPaymentProviderCache _providers;
    private readonly IPaymentWebhookInboxRepository _inbox;
    private readonly IWebhookNormalizerResolver _normalizers;
    private readonly IWebhookSignatureVerifierResolver _verifiers;
    private readonly IWebhookTenantResolver _tenantResolver;
    private readonly IPaymentWorkDispatcher _workDispatcher;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentWebhookIntakeService> _logger;

    public PaymentWebhookIntakeService(
        IPaymentRepository payments,
        IPaymentRefundRepository refunds,
        IPaymentCaptureRepository captures,
        IPaymentProviderCache providers,
        IPaymentWebhookInboxRepository inbox,
        IWebhookNormalizerResolver normalizers,
        IWebhookSignatureVerifierResolver verifiers,
        IWebhookTenantResolver tenantResolver,
        IPaymentWorkDispatcher workDispatcher,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentWebhookIntakeService> logger)
    {
        _payments = payments;
        _refunds = refunds;
        _captures = captures;
        _providers = providers;
        _inbox = inbox;
        _normalizers = normalizers;
        _verifiers = verifiers;
        _tenantResolver = tenantResolver;
        _workDispatcher = workDispatcher;
        _options = options;
        _logger = logger;
    }

    public async Task<WebhookIntakeOutcome> AcceptAsync(
        string providerName,
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken shutdownToken)
    {
        var stopwatch = Stopwatch.StartNew();

        using var providerScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Provider"] = PaymentLogValue.Label(providerName)
        });

        var normalizer = _normalizers.Resolve(providerName);
        var verifier = _verifiers.Resolve(providerName);

        if (normalizer == null || verifier == null)
        {
            _logger.LogWarning(
                "Webhook intake rejected Reason=provider_not_registered HasNormalizer={HasNormalizer} HasVerifier={HasVerifier}",
                normalizer != null,
                verifier != null);

            return WebhookIntakeOutcome.NotFound;
        }

        var parsed = normalizer.Parse(rawBody, headers);

        if (!parsed.IsValid)
        {
            _logger.LogWarning(
                "Webhook intake rejected Reason={Reason} DurationMs={DurationMs}",
                parsed.RejectionReason,
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.Malformed;
        }

        _logger.LogInformation(
            "Webhook intake parsed EventCount={EventCount}",
            parsed.Events.Count);

        using var timeout = CreateProcessingTimeout(shutdownToken);

        try
        {
            var admitted = new List<AdmittedWebhook>(parsed.Events.Count);

            for (var index = 0; index < parsed.Events.Count; index++)
            {
                var admission = await AdmitAsync(
                    providerName,
                    verifier,
                    parsed.Events[index],
                    index,
                    timeout.Token);

                if (admission.Outcome != WebhookIntakeOutcome.Accepted)
                {
                    return admission.Outcome;
                }

                admitted.Add(admission.Webhook!);
            }

            var stored = 0;

            foreach (var webhook in admitted)
            {
                if (await StoreAsync(webhook, timeout.Token) == WebhookStoreResult.Stored)
                {
                    stored++;
                }
            }

            foreach (var tenantId in admitted
                         .Select(webhook => webhook.TenantId)
                         .Distinct(StringComparer.Ordinal))
            {
                await _workDispatcher.TryDispatchAsync(
                    tenantId,
                    includeRecovery: false,
                    cancellationToken: timeout.Token);
            }

            _logger.LogInformation(
                "Webhook intake completed Outcome=Accepted AdmittedCount={AdmittedCount} StoredCount={StoredCount} DuplicateCount={DuplicateCount} DurationMs={DurationMs}",
                admitted.Count,
                stored,
                admitted.Count - stored,
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.Accepted;
        }
        catch (OperationCanceledException) when (!shutdownToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Webhook intake timed out TimeoutSeconds={TimeoutSeconds} DurationMs={DurationMs}",
                GetIntakeTimeoutSeconds(),
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.StorageUnavailable;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Webhook intake cancelled Reason=application_stopping");

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Webhook intake failed Outcome=StorageUnavailable ExceptionType={ExceptionType} DurationMs={DurationMs}",
                exception.GetType().Name,
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.StorageUnavailable;
        }
    }

    private async Task<(WebhookIntakeOutcome Outcome, AdmittedWebhook? Webhook)> AdmitAsync(
        string providerName,
        IWebhookSignatureVerifier verifier,
        ParsedWebhookEvent webhookEvent,
        int index,
        CancellationToken cancellationToken)
    {
        var isStoredMethod = webhookEvent.Intent == WebhookIntent.StoredMethod;
        PaymentWebhookRoute? route = null;
        string tenantId;

        if (isStoredMethod)
        {
            if (!_tenantResolver.TryResolveTenant(
                    webhookEvent.RoutingReference,
                    out tenantId))
            {
                return Reject(index, webhookEvent, "shopper_reference_route_invalid");
            }
        }
        else
        {
            if (!_tenantResolver.TryResolvePayment(
                    webhookEvent.RoutingReference,
                    out var resolved))
            {
                return Reject(index, webhookEvent, "merchant_reference_route_invalid");
            }

            route = resolved;
            tenantId = resolved.TenantId;
        }

        using var scope = BeginRouteScope(
            tenantId,
            route?.PaymentDetailId,
            webhookEvent);

        var provider = await GetProviderAsync(
            providerName,
            tenantId,
            cancellationToken);

        if (provider == null || !provider.IsEnabled)
        {
            _logger.LogWarning(
                "Webhook event rejected Index={Index} Reason=provider_missing_or_disabled",
                index);

            return (WebhookIntakeOutcome.NotFound, null);
        }

        var signature = verifier.Verify(provider, webhookEvent.Signature);

        if (signature != WebhookSignatureOutcome.Valid)
        {
            provider = await RefreshProviderAsync(
                providerName,
                tenantId,
                cancellationToken);

            if (provider == null ||
                !provider.IsEnabled ||
                verifier.Verify(provider, webhookEvent.Signature) != WebhookSignatureOutcome.Valid)
            {
                _logger.LogWarning(
                    "Webhook event rejected Index={Index} Reason=signature_rejected FirstOutcome={FirstOutcome}",
                    index,
                    signature);

                return (WebhookIntakeOutcome.Unauthorized, null);
            }
        }

        var payload = webhookEvent.Payload;

        if (!string.Equals(
                payload.MerchantAccount,
                provider.MerchantId,
                StringComparison.Ordinal))
        {
            return RejectUnauthorized(index, "merchant_account_mismatch");
        }

        if (webhookEvent.EchoedTenantId != null &&
            !string.Equals(webhookEvent.EchoedTenantId, tenantId, StringComparison.Ordinal))
        {
            return RejectUnauthorized(index, "tenant_metadata_mismatch");
        }

        if (isStoredMethod)
        {
            if (string.IsNullOrWhiteSpace(payload.StoredPaymentMethodToken) ||
                string.IsNullOrWhiteSpace(payload.ShopperReference))
            {
                return RejectUnauthorized(index, "token_ownership_invalid");
            }

            return (
                WebhookIntakeOutcome.Accepted,
                new AdmittedWebhook(tenantId, providerName, webhookEvent));
        }

        var ownership = await VerifyPaymentOwnershipAsync(
            providerName,
            provider,
            route!,
            webhookEvent,
            index,
            cancellationToken);

        return ownership;
    }

    private async Task<(WebhookIntakeOutcome Outcome, AdmittedWebhook? Webhook)> VerifyPaymentOwnershipAsync(
        string providerName,
        PaymentProvider provider,
        PaymentWebhookRoute route,
        ParsedWebhookEvent webhookEvent,
        int index,
        CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(route, cancellationToken);

        if (payment == null)
        {
            _logger.LogWarning(
                "Webhook event rejected Index={Index} Reason=payment_not_found",
                index);

            return (WebhookIntakeOutcome.NotFound, null);
        }

        var payload = webhookEvent.Payload;
        var refund = string.IsNullOrWhiteSpace(route.RefundId)
            ? null
            : payment.Refunds.FirstOrDefault(candidate => candidate.RefundId == route.RefundId);
        var capture = string.IsNullOrWhiteSpace(route.CaptureId)
            ? null
            : payment.Captures.FirstOrDefault(candidate => candidate.CaptureId == route.CaptureId);

        var expectedReference =
            refund?.ProviderReference ??
            capture?.ProviderReference ??
            payment.ProviderReference ??
            payment.InitiationRequest?.Reference;
        var expectedMerchant =
            refund?.ProviderMerchantAccount ??
            capture?.ProviderMerchantAccount ??
            payment.ProviderMerchantAccount ??
            payment.InitiationRequest?.MerchantAccount;

        if (!string.Equals(expectedReference, payload.MerchantReference, StringComparison.Ordinal))
        {
            return RejectUnauthorized(index, "payment_reference_mismatch");
        }

        if (!string.Equals(expectedMerchant, payload.MerchantAccount, StringComparison.Ordinal))
        {
            return RejectUnauthorized(index, "payment_merchant_mismatch");
        }

        if (!string.Equals(payment.ProviderName, provider.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return RejectUnauthorized(index, "payment_provider_mismatch");
        }

        if (refund != null && webhookEvent.Intent != WebhookIntent.Refund)
        {
            return RejectUnauthorized(index, "refund_reference_event_mismatch");
        }

        if (capture != null && webhookEvent.Intent != WebhookIntent.Capture)
        {
            return RejectUnauthorized(index, "capture_reference_event_mismatch");
        }

        if (refund != null &&
            !string.Equals(
                refund.OriginalPaymentPspReference,
                payload.OriginalPspReference,
                StringComparison.Ordinal))
        {
            return RejectUnauthorized(index, "refund_original_reference_mismatch");
        }

        if (capture != null &&
            !string.Equals(
                capture.OriginalPaymentPspReference,
                payload.OriginalPspReference,
                StringComparison.Ordinal))
        {
            return RejectUnauthorized(index, "capture_original_reference_mismatch");
        }

        payload.PaymentDetailId = payment.ItemId;
        payload.RefundId = refund?.RefundId;
        payload.CaptureId = capture?.CaptureId;

        _logger.LogInformation(
            "Webhook event admitted Index={Index} Intent={Intent}",
            index,
            webhookEvent.Intent);

        return (
            WebhookIntakeOutcome.Accepted,
            new AdmittedWebhook(route.TenantId, providerName, webhookEvent));
    }

    private Task<PaymentDetail?> LoadPaymentAsync(
        PaymentWebhookRoute route,
        CancellationToken cancellationToken) =>
        !string.IsNullOrWhiteSpace(route.RefundId)
            ? _refunds.GetPaymentByRefundIdAsync(route.TenantId, route.RefundId, cancellationToken)
            : !string.IsNullOrWhiteSpace(route.CaptureId)
                ? _captures.GetPaymentByCaptureIdAsync(route.TenantId, route.CaptureId, cancellationToken)
                : _payments.GetByIdAsync(route.TenantId, route.PaymentDetailId, cancellationToken);

    private async Task<WebhookStoreResult> StoreAsync(
        AdmittedWebhook webhook,
        CancellationToken cancellationToken)
    {
        var record = new PaymentWebhookInbox
        {
            TenantId = webhook.TenantId,
            ProviderName = webhook.ProviderName,
            WebhookType = webhook.Event.WebhookType,
            EventCode = webhook.Event.EventCode,
            Intent = webhook.Event.Intent,
            PspReference = webhook.Event.Payload.PspReference,
            MerchantReference = webhook.Event.Payload.MerchantReference,
            EventDateUtc = webhook.Event.EventDateUtc,
            DeduplicationKey = PaymentHashing.HashSensitiveValue(
                $"{webhook.TenantId}:{webhook.Event.DeduplicationSeed}"),
            NormalizedPayload = webhook.Event.Payload
        };

        var result = await _inbox.StoreAsync(record, cancellationToken);

        _logger.LogInformation(
            "Webhook inbox persistence completed WebhookIdHash={WebhookIdHash} StoreResult={StoreResult}",
            PaymentLogValue.Hash(record.WebhookId),
            result);

        return result;
    }

    private Task<PaymentProvider?> GetProviderAsync(
        string providerName,
        string tenantId,
        CancellationToken cancellationToken) =>
        _providers.GetAsync(
            tenantId,
            providerName,
            () => _payments.GetProviderAsync(tenantId, providerName, cancellationToken));

    private Task<PaymentProvider?> RefreshProviderAsync(
        string providerName,
        string tenantId,
        CancellationToken cancellationToken) =>
        _providers.RefreshAsync(
            tenantId,
            providerName,
            () => _payments.GetProviderAsync(tenantId, providerName, cancellationToken));

    private (WebhookIntakeOutcome, AdmittedWebhook?) Reject(
        int index,
        ParsedWebhookEvent webhookEvent,
        string reason)
    {
        _logger.LogWarning(
            "Webhook event rejected Index={Index} Reason={Reason} EventCode={EventCode}",
            index,
            reason,
            PaymentLogValue.Label(webhookEvent.EventCode));

        return (WebhookIntakeOutcome.Malformed, null);
    }

    private (WebhookIntakeOutcome, AdmittedWebhook?) RejectUnauthorized(
        int index,
        string reason)
    {
        _logger.LogWarning(
            "Webhook event rejected Index={Index} Reason={Reason}",
            index,
            reason);

        return (WebhookIntakeOutcome.Unauthorized, null);
    }

    private CancellationTokenSource CreateProcessingTimeout(CancellationToken shutdownToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(GetIntakeTimeoutSeconds()));

        return timeout;
    }

    private int GetIntakeTimeoutSeconds() => Math.Clamp(
        _options.CurrentValue.WebhookIntakeTimeoutSeconds,
        1,
        60);

    private IDisposable? BeginRouteScope(
        string tenantId,
        string? paymentDetailId,
        ParsedWebhookEvent webhookEvent) =>
        _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantHash"] = PaymentLogValue.Hash(tenantId),
            ["PaymentDetailIdHash"] = PaymentLogValue.Hash(paymentDetailId),
            ["ProviderEventIdHash"] = PaymentLogValue.Hash(webhookEvent.ProviderEventId),
            ["EventCode"] = PaymentLogValue.Label(webhookEvent.EventCode)
        });

    private sealed record AdmittedWebhook(
        string TenantId,
        string ProviderName,
        ParsedWebhookEvent Event);
}
