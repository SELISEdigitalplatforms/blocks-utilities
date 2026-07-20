using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentWebhookIntakeService : IPaymentWebhookIntakeService
{
    private static readonly HashSet<string> TokenEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "recurring.token.created",
        "recurring.token.alreadyExisting",
        "recurring.token.updated",
        "recurring.token.disabled"
    };

    private readonly IPaymentRepository _payments;
    private readonly IPaymentRefundRepository _refunds;
    private readonly IPaymentProviderCache _providers;
    private readonly IPaymentWebhookInboxRepository _inbox;
    private readonly IWebhookSignatureValidator _signatures;
    private readonly IWebhookTenantResolver _tenantResolver;
    private readonly IWebhookPayloadFactory _payloads;
    private readonly IPaymentWorkDispatcher _workDispatcher;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentWebhookIntakeService> _logger;

    public PaymentWebhookIntakeService(
        IPaymentRepository payments,
        IPaymentRefundRepository refunds,
        IPaymentProviderCache providers,
        IPaymentWebhookInboxRepository inbox,
        IWebhookSignatureValidator signatures,
        IWebhookTenantResolver tenantResolver,
        IWebhookPayloadFactory payloads,
        IPaymentWorkDispatcher workDispatcher,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentWebhookIntakeService> logger)
    {
        _payments = payments;
        _refunds = refunds;
        _providers = providers;
        _inbox = inbox;
        _signatures = signatures;
        _tenantResolver = tenantResolver;
        _payloads = payloads;
        _workDispatcher = workDispatcher;
        _options = options;
        _logger = logger;
    }

    public async Task<WebhookIntakeOutcome> AcceptStandardAsync(
        StandardWebhookRequest request,
        CancellationToken shutdownToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var notificationItems = request.NotificationItems;

        _logger.LogInformation(
            "Standard webhook intake started NotificationCount={NotificationCount}",
            notificationItems?.Count ?? 0);

        if (notificationItems == null ||
            notificationItems.Count is 0 or > 100 ||
            notificationItems.Any(container => container.Item == null))
        {
            _logger.LogWarning(
                "Standard webhook intake rejected Reason=invalid_notification_collection NotificationCount={NotificationCount} DurationMs={DurationMs}",
                notificationItems?.Count ?? 0,
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.Malformed;
        }

        using var timeout = CreateProcessingTimeout(shutdownToken);

        try
        {
            var validated = new List<ValidatedStandardWebhook>(
                notificationItems.Count);
            var itemIndex = 0;

            foreach (var container in notificationItems)
            {
                var validation = await ValidateStandardAsync(
                    container.Item!,
                    itemIndex,
                    timeout.Token);

                if (validation.Outcome != WebhookIntakeOutcome.Accepted)
                {
                    _logger.LogWarning(
                        "Standard webhook intake stopped ItemIndex={ItemIndex} Outcome={Outcome} DurationMs={DurationMs}",
                        itemIndex,
                        validation.Outcome,
                        stopwatch.Elapsed.TotalMilliseconds);

                    return validation.Outcome;
                }

                validated.Add(validation.Webhook!);
                itemIndex++;
            }

            var storedCount = 0;
            var duplicateCount = 0;

            foreach (var webhook in validated)
            {
                var storeResult = await StoreStandardAsync(
                    webhook,
                    timeout.Token);

                if (storeResult == WebhookStoreResult.Stored)
                {
                    storedCount++;
                }
                else
                {
                    duplicateCount++;
                }
            }

            foreach (var tenantId in validated
                         .Select(webhook => webhook.TenantId)
                         .Distinct(StringComparer.Ordinal))
            {
                await _workDispatcher.TryDispatchAsync(
                    tenantId,
                    includeRecovery: false,
                    cancellationToken: timeout.Token);
            }

            _logger.LogInformation(
                "Standard webhook intake completed Outcome=Accepted ValidatedCount={ValidatedCount} StoredCount={StoredCount} DuplicateCount={DuplicateCount} DurationMs={DurationMs}",
                validated.Count,
                storedCount,
                duplicateCount,
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.Accepted;
        }
        catch (OperationCanceledException) when (!shutdownToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Standard webhook intake timed out TimeoutSeconds={TimeoutSeconds} DurationMs={DurationMs}",
                GetIntakeTimeoutSeconds(),
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.StorageUnavailable;
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Standard webhook intake cancelled Reason=application_stopping DurationMs={DurationMs}",
                stopwatch.Elapsed.TotalMilliseconds);

            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Standard webhook intake failed Outcome=StorageUnavailable ExceptionType={ExceptionType} DurationMs={DurationMs}",
                exception.GetType().Name,
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.StorageUnavailable;
        }
    }

    public async Task<WebhookIntakeOutcome> AcceptTokenAsync(
        string rawBody,
        string signature,
        CancellationToken shutdownToken)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Token webhook intake started BodyBytes={BodyBytes} HasSignature={HasSignature}",
            System.Text.Encoding.UTF8.GetByteCount(rawBody ?? string.Empty),
            !string.IsNullOrWhiteSpace(signature));

        if (string.IsNullOrWhiteSpace(rawBody) ||
            string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogWarning(
                "Token webhook intake rejected Reason=missing_body_or_signature DurationMs={DurationMs}",
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.Malformed;
        }

        using var timeout = CreateProcessingTimeout(shutdownToken);

        try
        {
            var request = JsonSerializer.Deserialize<TokenWebhookRequest>(
                rawBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            if (!IsValidTokenRequest(request))
            {
                _logger.LogWarning(
                    "Token webhook intake rejected Reason=invalid_event_envelope EventType={EventType} EventIdHash={EventIdHash} DurationMs={DurationMs}",
                    PaymentLogValue.Label(request?.Type),
                    PaymentLogValue.Hash(request?.EffectiveEventId),
                    stopwatch.Elapsed.TotalMilliseconds);

                return WebhookIntakeOutcome.Malformed;
            }

            if (!_tenantResolver.TryResolveToken(request!, out var tenantId))
            {
                _logger.LogWarning(
                    "Token webhook intake rejected Reason=shopper_reference_route_invalid EventType={EventType} EventIdHash={EventIdHash} DurationMs={DurationMs}",
                    PaymentLogValue.Label(request!.Type),
                    PaymentLogValue.Hash(request.EffectiveEventId),
                    stopwatch.Elapsed.TotalMilliseconds);

                return WebhookIntakeOutcome.Malformed;
            }

            using var scope = BeginRouteScope(
                tenantId,
                null,
                request!.EffectiveEventId,
                request.Type);

            _logger.LogInformation(
                "Token webhook tenant route resolved; loading provider configuration");

            var provider = await GetProviderAsync(
                tenantId,
                timeout.Token);

            if (provider == null || !provider.IsEnabled)
            {
                _logger.LogWarning(
                    "Token webhook intake rejected Reason=provider_missing_or_disabled");

                return WebhookIntakeOutcome.NotFound;
            }

            _logger.LogInformation(
                "Token webhook provider configuration loaded Provider={Provider}",
                PaymentLogValue.Label(provider.ProviderName));

            if (!ValidateTokenSignature(
                    provider,
                    rawBody,
                    signature))
            {
                provider = await RefreshProviderAsync(
                    tenantId,
                    timeout.Token);

                if (provider == null ||
                    !provider.IsEnabled ||
                    !ValidateTokenSignature(
                        provider,
                        rawBody,
                        signature))
                {
                    _logger.LogWarning(
                        "Token webhook intake rejected Reason=signature_invalid_after_secret_refresh");

                    return WebhookIntakeOutcome.Unauthorized;
                }
            }

            _logger.LogInformation(
                "Token webhook signature validated");

            var payload = _payloads.CreateToken(
                provider.ProviderName,
                request!);

            if (!string.Equals(
                    payload.MerchantAccount,
                    provider.MerchantId,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(payload.StoredPaymentMethodToken) ||
                string.IsNullOrWhiteSpace(payload.ShopperReference))
            {
                _logger.LogWarning(
                    "Token webhook intake rejected Reason=merchant_or_token_ownership_invalid MerchantMatched={MerchantMatched} HasStoredMethod={HasStoredMethod} HasShopperReference={HasShopperReference}",
                    string.Equals(
                        payload.MerchantAccount,
                        provider.MerchantId,
                        StringComparison.Ordinal),
                    !string.IsNullOrWhiteSpace(payload.StoredPaymentMethodToken),
                    !string.IsNullOrWhiteSpace(payload.ShopperReference));

                return WebhookIntakeOutcome.Unauthorized;
            }

            var inboxRecord = new PaymentWebhookInbox
            {
                TenantId = tenantId,
                WebhookType = "token",
                EventCode = request!.Type!,
                EventDateUtc = request.CreatedAt?.ToUniversalTime() ?? DateTime.UtcNow,
                DeduplicationKey = PaymentHashing.HashSensitiveValue(
                    $"{tenantId}:{request.EffectiveEventId}:{request.Type}"),
                NormalizedPayload = payload
            };

            _logger.LogInformation(
                "Token webhook inbox persistence started WebhookIdHash={WebhookIdHash} DeduplicationHash={DeduplicationHash}",
                PaymentLogValue.Hash(inboxRecord.WebhookId),
                PaymentLogValue.Hash(inboxRecord.DeduplicationKey));

            var storeResult = await _inbox.StoreAsync(
                inboxRecord,
                timeout.Token);

            await _workDispatcher.TryDispatchAsync(
                tenantId,
                includeRecovery: false,
                cancellationToken: timeout.Token);

            _logger.LogInformation(
                "Token webhook intake completed Outcome=Accepted StoreResult={StoreResult} WebhookIdHash={WebhookIdHash} DurationMs={DurationMs}",
                storeResult,
                PaymentLogValue.Hash(inboxRecord.WebhookId),
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.Accepted;
        }
        catch (JsonException)
        {
            _logger.LogWarning(
                "Token webhook intake rejected Reason=invalid_json DurationMs={DurationMs}",
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.Malformed;
        }
        catch (OperationCanceledException) when (!shutdownToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Token webhook intake timed out TimeoutSeconds={TimeoutSeconds} DurationMs={DurationMs}",
                GetIntakeTimeoutSeconds(),
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.StorageUnavailable;
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Token webhook intake cancelled Reason=application_stopping DurationMs={DurationMs}",
                stopwatch.Elapsed.TotalMilliseconds);

            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Token webhook intake failed Outcome=StorageUnavailable ExceptionType={ExceptionType} DurationMs={DurationMs}",
                exception.GetType().Name,
                stopwatch.Elapsed.TotalMilliseconds);

            return WebhookIntakeOutcome.StorageUnavailable;
        }
    }

    private async Task<(WebhookIntakeOutcome Outcome, ValidatedStandardWebhook? Webhook)> ValidateStandardAsync(
        NotificationItem item,
        int itemIndex,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Standard webhook item validation started ItemIndex={ItemIndex} EventCode={EventCode} PspReferenceHash={PspReferenceHash}",
            itemIndex,
            PaymentLogValue.Label(item.EventCode),
            PaymentLogValue.Hash(item.PspReference));

        if (string.IsNullOrWhiteSpace(item.PspReference))
        {
            return RejectStandard(itemIndex, item, "missing_psp_reference");
        }

        if (string.IsNullOrWhiteSpace(item.EventCode))
        {
            return RejectStandard(itemIndex, item, "missing_event_code");
        }

        if (!bool.TryParse(item.Success, out var success))
        {
            return RejectStandard(itemIndex, item, "invalid_success_value");
        }

        if (!_tenantResolver.TryResolveStandard(item, out var route))
        {
            return RejectStandard(itemIndex, item, "merchant_reference_route_invalid");
        }

        using var scope = BeginRouteScope(
            route.TenantId,
            route.PaymentDetailId,
            item.PspReference,
            item.EventCode);

        _logger.LogInformation(
            "Standard webhook tenant and payment route resolved ItemIndex={ItemIndex}; loading provider configuration",
            itemIndex);

        var provider = await GetProviderAsync(
            route.TenantId,
            cancellationToken);

        if (provider == null || !provider.IsEnabled)
        {
            _logger.LogWarning(
                "Standard webhook item rejected ItemIndex={ItemIndex} Reason=provider_missing_or_disabled",
                itemIndex);

            return (WebhookIntakeOutcome.NotFound, null);
        }

        _logger.LogInformation(
            "Standard webhook provider configuration loaded ItemIndex={ItemIndex} Provider={Provider}",
            itemIndex,
            PaymentLogValue.Label(provider.ProviderName));

        if (!ValidateStandardSignature(provider, item))
        {
            provider = await RefreshProviderAsync(
                route.TenantId,
                cancellationToken);

            if (provider == null ||
                !provider.IsEnabled ||
                !ValidateStandardSignature(provider, item))
            {
                _logger.LogWarning(
                    "Standard webhook item rejected ItemIndex={ItemIndex} Reason=signature_invalid_after_secret_refresh",
                    itemIndex);

                return (WebhookIntakeOutcome.Unauthorized, null);
            }
        }

        _logger.LogInformation(
            "Standard webhook HMAC signature validated ItemIndex={ItemIndex}",
            itemIndex);

        if (!string.Equals(
                item.MerchantAccountCode,
                provider.MerchantId,
                StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Standard webhook item rejected ItemIndex={ItemIndex} Reason=merchant_account_mismatch",
                itemIndex);

            return (WebhookIntakeOutcome.Unauthorized, null);
        }

        if (!_tenantResolver.IsMetadataConsistent(item, route.TenantId))
        {
            _logger.LogWarning(
                "Standard webhook item rejected ItemIndex={ItemIndex} Reason=tenant_metadata_mismatch",
                itemIndex);

            return (WebhookIntakeOutcome.Unauthorized, null);
        }

        _logger.LogInformation(
            "Standard webhook merchant and tenant metadata validated ItemIndex={ItemIndex}; loading payment",
            itemIndex);

        var payment = string.IsNullOrWhiteSpace(
                route.RefundId)
            ? await _payments.GetByIdAsync(
                route.TenantId,
                route.PaymentDetailId,
                cancellationToken)
            : await _refunds.GetPaymentByRefundIdAsync(
                route.TenantId,
                route.RefundId,
                cancellationToken);

        if (payment == null)
        {
            _logger.LogWarning(
                "Standard webhook item rejected ItemIndex={ItemIndex} Reason=payment_not_found",
                itemIndex);

            return (WebhookIntakeOutcome.NotFound, null);
        }

        _logger.LogInformation(
            "Standard webhook payment loaded ItemIndex={ItemIndex} CurrentPaymentStatus={CurrentPaymentStatus}",
            itemIndex,
            PaymentLogValue.Label(payment.PaymentStatus));

        var refund = string.IsNullOrWhiteSpace(
                route.RefundId)
            ? null
            : payment.Refunds.FirstOrDefault(
                candidate =>
                    candidate.RefundId == route.RefundId);
        var expectedReference =
            refund?.ProviderReference ??
            payment.ProviderReference ??
            payment.InitiationRequest?.Reference;
        var expectedMerchant =
            refund?.ProviderMerchantAccount ??
            payment.ProviderMerchantAccount ??
            payment.InitiationRequest?.MerchantAccount;

        if (!string.Equals(
                expectedReference,
                item.MerchantReference,
                StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Standard webhook item rejected ItemIndex={ItemIndex} Reason=payment_reference_mismatch",
                itemIndex);

            return (WebhookIntakeOutcome.Unauthorized, null);
        }

        if (!string.Equals(
                expectedMerchant,
                item.MerchantAccountCode,
                StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Standard webhook item rejected ItemIndex={ItemIndex} Reason=payment_merchant_mismatch",
                itemIndex);

            return (WebhookIntakeOutcome.Unauthorized, null);
        }

        if (!string.Equals(
                payment.ProviderName,
                provider.ProviderName,
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Standard webhook item rejected ItemIndex={ItemIndex} Reason=payment_provider_mismatch",
                itemIndex);

            return (WebhookIntakeOutcome.Unauthorized, null);
        }

        if (refund != null &&
            !IsRefundEvent(item.EventCode))
        {
            _logger.LogWarning(
                "Standard webhook item rejected ItemIndex={ItemIndex} Reason=refund_reference_event_mismatch",
                itemIndex);

            return (WebhookIntakeOutcome.Unauthorized, null);
        }

        if (refund != null &&
            !string.Equals(
                refund.OriginalPaymentPspReference,
                item.OriginalReference,
                StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Standard webhook item rejected ItemIndex={ItemIndex} Reason=refund_original_reference_mismatch",
                itemIndex);

            return (WebhookIntakeOutcome.Unauthorized, null);
        }

        _logger.LogInformation(
            "Standard webhook item validation completed ItemIndex={ItemIndex} Outcome=Accepted ProviderSuccess={ProviderSuccess}",
            itemIndex,
            success);

        return (
            WebhookIntakeOutcome.Accepted,
            new ValidatedStandardWebhook(
                route.TenantId,
                payment.ItemId,
                provider.ProviderName,
                item,
                success,
                refund?.RefundId));
    }

    private async Task<WebhookStoreResult> StoreStandardAsync(
        ValidatedStandardWebhook webhook,
        CancellationToken cancellationToken)
    {
        var item = webhook.Item;
        var payload = _payloads.CreateStandard(
            webhook.ProviderName,
            webhook.PaymentDetailId,
            item,
            webhook.Success,
            webhook.RefundId);

        var inboxRecord = new PaymentWebhookInbox
        {
            TenantId = webhook.TenantId,
            WebhookType = "standard",
            EventCode = item.EventCode!,
            PspReference = item.PspReference,
            MerchantReference = item.MerchantReference,
            EventDateUtc = item.EventDate?.ToUniversalTime() ?? DateTime.UtcNow,
            DeduplicationKey = PaymentHashing.HashSensitiveValue(
                $"{webhook.TenantId}:{item.PspReference}:{item.EventCode}:{webhook.Success}"),
            NormalizedPayload = payload
        };

        using var scope = BeginRouteScope(
            webhook.TenantId,
            webhook.PaymentDetailId,
            item.PspReference,
            item.EventCode);

        _logger.LogInformation(
            "Standard webhook inbox persistence started WebhookIdHash={WebhookIdHash} DeduplicationHash={DeduplicationHash}",
            PaymentLogValue.Hash(inboxRecord.WebhookId),
            PaymentLogValue.Hash(inboxRecord.DeduplicationKey));

        var result = await _inbox.StoreAsync(
            inboxRecord,
            cancellationToken);

        _logger.LogInformation(
            "Standard webhook inbox persistence completed WebhookIdHash={WebhookIdHash} StoreResult={StoreResult}",
            PaymentLogValue.Hash(inboxRecord.WebhookId),
            result);

        return result;
    }

    private Task<PaymentProvider?> GetProviderAsync(
        string tenantId,
        CancellationToken cancellationToken) =>
        _providers.GetAsync(
            tenantId,
            PaymentConstants.AdyenOnlineProvider,
            () => _payments.GetProviderAsync(
                tenantId,
                PaymentConstants.AdyenOnlineProvider,
                cancellationToken));

    private Task<PaymentProvider?> RefreshProviderAsync(
        string tenantId,
        CancellationToken cancellationToken) =>
        _providers.RefreshAsync(
            tenantId,
            PaymentConstants.AdyenOnlineProvider,
            () => _payments.GetProviderAsync(
                tenantId,
                PaymentConstants.AdyenOnlineProvider,
                cancellationToken));

    private bool ValidateTokenSignature(
        PaymentProvider provider,
        string rawBody,
        string? signature) =>
        !string.IsNullOrWhiteSpace(
            provider.TokenWebhookHmacKey) &&
        _signatures.ValidateToken(
            rawBody,
            signature ?? string.Empty,
            provider.TokenWebhookHmacKey,
            provider.PreviousTokenWebhookHmacKey);

    private bool ValidateStandardSignature(
        PaymentProvider provider,
        NotificationItem item) =>
        !string.IsNullOrWhiteSpace(
            provider.StandardWebhookHmacKey) &&
        _signatures.ValidateStandard(
            item,
            provider.StandardWebhookHmacKey,
            provider.PreviousStandardWebhookHmacKey);

    private static bool IsValidTokenRequest(TokenWebhookRequest? request) =>
        request != null &&
        !string.IsNullOrWhiteSpace(request.EffectiveEventId) &&
        !string.IsNullOrWhiteSpace(request.Type) &&
        TokenEvents.Contains(request.Type) &&
        request.Data.ValueKind == JsonValueKind.Object;

    private static bool IsRefundEvent(string? eventCode) =>
        eventCode is not null &&
        (eventCode.Equals(
             "REFUND",
             StringComparison.OrdinalIgnoreCase) ||
         eventCode.Equals(
             "REFUND_FAILED",
             StringComparison.OrdinalIgnoreCase) ||
         eventCode.Equals(
             "REFUNDED_REVERSED",
             StringComparison.OrdinalIgnoreCase));

    private CancellationTokenSource CreateProcessingTimeout(
        CancellationToken shutdownToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            shutdownToken);
        var timeoutSeconds = GetIntakeTimeoutSeconds();

        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        return timeout;
    }

    private int GetIntakeTimeoutSeconds() => Math.Clamp(
        _options.CurrentValue.WebhookIntakeTimeoutSeconds,
        1,
        60);

    private IDisposable? BeginRouteScope(
        string tenantId,
        string? paymentDetailId,
        string? providerEventId,
        string? eventCode) =>
        _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantHash"] = PaymentLogValue.Hash(tenantId),
            ["PaymentDetailIdHash"] = PaymentLogValue.Hash(paymentDetailId),
            ["ProviderEventIdHash"] = PaymentLogValue.Hash(providerEventId),
            ["EventCode"] = PaymentLogValue.Label(eventCode)
        });

    private (WebhookIntakeOutcome Outcome, ValidatedStandardWebhook? Webhook) RejectStandard(
        int itemIndex,
        NotificationItem item,
        string reason)
    {
        _logger.LogWarning(
            "Standard webhook item rejected ItemIndex={ItemIndex} Reason={Reason} EventCode={EventCode} PspReferenceHash={PspReferenceHash}",
            itemIndex,
            reason,
            PaymentLogValue.Label(item.EventCode),
            PaymentLogValue.Hash(item.PspReference));

        return (WebhookIntakeOutcome.Malformed, null);
    }

}
