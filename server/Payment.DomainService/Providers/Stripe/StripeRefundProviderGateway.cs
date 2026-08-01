using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.Refunds;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Returns money through Stripe: a refund against a settled charge, or a cancellation of an
/// authorization that was never captured.
/// </summary>
/// <remarks>
/// Stripe takes no merchant account as a request field — the API key already identifies the
/// account — but both it and the reference this service minted travel in the refund's own
/// metadata, because that is all the resulting event carries to route and authorize it back to
/// this refund record.
/// </remarks>
public sealed class StripeRefundProviderGateway : IPaymentRefundProviderGateway
{
    private readonly IHttpService _httpService;
    private readonly StripeEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<StripeRefundProviderGateway> _logger;

    public StripeRefundProviderGateway(
        IHttpService httpService,
        StripeEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<StripeRefundProviderGateway> logger)
    {
        _httpService = httpService;
        _endpointPolicy = endpointPolicy;
        _options = options;
        _logger = logger;
    }

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.StripeProvider,
            StringComparison.OrdinalIgnoreCase);

    public async Task<PaymentRefundProviderResult> SubmitAsync(
        PaymentProvider provider,
        string originalPaymentPspReference,
        ProviderRefundRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);

        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl))
        {
            return new PaymentRefundProviderResult(
                PaymentRefundProviderOutcome.Unavailable);
        }

        var form = new StripeForm()
            .Add("payment_intent", originalPaymentPspReference)
            .Add("amount", request.Amount.Value)
            .AddMetadata(
                StripeRoutingMetadata.ForOperation(
                    request.Reference,
                    request.MerchantAccount,
                    request.OrganizationId));

        try
        {
            var (refund, error) = await _httpService.SendFormUrlEncoded<StripeRefund>(
                HttpMethod.Post,
                form.Fields,
                StripeUrl.Build(provider.ApiBaseUrl, "v1/refunds"),
                StripeRequestHeaders.Create(provider, idempotencyKey),
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

            if (refund is { Id: not null, Error: null })
            {
                return IsTerminalFailure(refund.Status)
                    ? new PaymentRefundProviderResult(
                        PaymentRefundProviderOutcome.Rejected,
                        SafeErrorCode: ProviderRejectionParser.SanitizeErrorCode(
                            refund.FailureReason ?? refund.Status))
                    : new PaymentRefundProviderResult(
                        PaymentRefundProviderOutcome.Submitted,
                        refund.Id,
                        refund.Status);
            }

            return Classify(provider, refund?.Error, error, "refund");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PaymentRefundProviderResult(PaymentRefundProviderOutcome.Timeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Payment refund provider call failed Provider={Provider} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return new PaymentRefundProviderResult(
                PaymentRefundProviderOutcome.OutcomeUnknown);
        }
    }

    /// <summary>
    /// Cancels an uncaptured authorization. Stripe answers with the intent already in its
    /// final state, so there is no separate reversal object to track.
    /// </summary>
    /// <remarks>
    /// Cancelling writes no metadata of its own — the intent already carries the payment's
    /// routing reference, and overwriting that key would strip the payment of the only handle
    /// its own events are routed by.
    /// </remarks>
    public async Task<PaymentRefundProviderResult> SubmitReversalAsync(
        PaymentProvider provider,
        string originalPaymentPspReference,
        ProviderReversalRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);

        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl))
        {
            return new PaymentRefundProviderResult(
                PaymentRefundProviderOutcome.Unavailable);
        }

        var url = StripeUrl.Build(
            provider.ApiBaseUrl,
            $"v1/payment_intents/{Uri.EscapeDataString(originalPaymentPspReference)}/cancel");

        try
        {
            var (intent, error) = await _httpService.SendFormUrlEncoded<StripePaymentIntent>(
                HttpMethod.Post,
                new Dictionary<string, string>(StringComparer.Ordinal),
                url,
                StripeRequestHeaders.Create(provider, idempotencyKey),
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

            if (intent is { Id: not null, Error: null })
            {
                return string.Equals(intent.Status, "canceled", StringComparison.Ordinal)
                    ? new PaymentRefundProviderResult(
                        PaymentRefundProviderOutcome.Settled,
                        intent.Id,
                        intent.Status)
                    : new PaymentRefundProviderResult(
                        PaymentRefundProviderOutcome.Rejected,
                        SafeErrorCode: ProviderRejectionParser.SanitizeErrorCode(intent.Status));
            }

            return Classify(provider, intent?.Error, error, "reversal");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PaymentRefundProviderResult(PaymentRefundProviderOutcome.Timeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Payment reversal provider call failed Provider={Provider} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return new PaymentRefundProviderResult(
                PaymentRefundProviderOutcome.OutcomeUnknown);
        }
    }

    /// <summary>
    /// A refund Stripe has already given up on. Anything else is still in flight and settles
    /// through <c>refund.updated</c>.
    /// </summary>
    private static bool IsTerminalFailure(string? status) =>
        string.Equals(status, "failed", StringComparison.Ordinal) ||
        string.Equals(status, "canceled", StringComparison.Ordinal);

    private PaymentRefundProviderResult Classify(
        PaymentProvider provider,
        StripeError? stripeError,
        string? transportError,
        string operation)
    {
        if (stripeError != null)
        {
            return new PaymentRefundProviderResult(
                StripeProviderOutcome.ToRefund(StripeOutcomeMapper.Map(stripeError)),
                SafeErrorCode: StripeOutcomeMapper.SafeCode(stripeError));
        }

        if (ProviderRejectionParser.TryGetValidationErrorCode(
                transportError,
                out var safeErrorCode))
        {
            return new PaymentRefundProviderResult(
                PaymentRefundProviderOutcome.Rejected,
                SafeErrorCode: safeErrorCode);
        }

        _logger.LogWarning(
            "Payment {Operation} returned no usable response Provider={Provider} HasPackageError={HasPackageError}",
            operation,
            PaymentLogValue.Label(provider.ProviderName),
            !string.IsNullOrWhiteSpace(transportError));

        return new PaymentRefundProviderResult(
            StripeUnavailable.IsTransient(transportError)
                ? PaymentRefundProviderOutcome.Unavailable
                : PaymentRefundProviderOutcome.OutcomeUnknown);
    }
}
