using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.Captures;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Captures an authorization that was taken with <c>capture_method=manual</c>.
/// </summary>
/// <remarks>
/// Stripe has no capture object: the intent is captured in place and returned in its new
/// state, so the reference reported back is the intent's. Deliberately sends no metadata —
/// capturing updates the intent, and writing the capture's reference into
/// <see cref="StripeRoutingMetadata.ReferenceKey"/> would overwrite the payment's own routing
/// reference and leave every later event for that payment unroutable.
/// </remarks>
public sealed class StripeCaptureProviderGateway : IPaymentCaptureProviderGateway
{
    private readonly IHttpService _httpService;
    private readonly StripeEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<StripeCaptureProviderGateway> _logger;

    public StripeCaptureProviderGateway(
        IHttpService httpService,
        StripeEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<StripeCaptureProviderGateway> logger)
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

    public async Task<PaymentCaptureProviderResult> SubmitAsync(
        PaymentProvider provider,
        string originalPaymentPspReference,
        ProviderCaptureRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);

        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl))
        {
            return new PaymentCaptureProviderResult(
                PaymentCaptureProviderOutcome.Unavailable);
        }

        var url = StripeUrl.Build(
            provider.ApiBaseUrl,
            $"v1/payment_intents/{Uri.EscapeDataString(originalPaymentPspReference)}/capture");
        var form = new StripeForm()
            .Add("amount_to_capture", request.Amount.Value);

        try
        {
            var (intent, error) = await _httpService.SendFormUrlEncoded<StripePaymentIntent>(
                HttpMethod.Post,
                form.Fields,
                url,
                StripeRequestHeaders.Create(provider, idempotencyKey),
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

            if (intent is { Id: not null, Error: null })
            {
                return ResolveOutcome(intent.Status) switch
                {
                    PaymentCaptureProviderOutcome.Settled =>
                        new PaymentCaptureProviderResult(
                            PaymentCaptureProviderOutcome.Settled,
                            intent.Id,
                            intent.Status),
                    PaymentCaptureProviderOutcome.Submitted =>
                        new PaymentCaptureProviderResult(
                            PaymentCaptureProviderOutcome.Submitted,
                            intent.Id,
                            intent.Status),
                    _ => new PaymentCaptureProviderResult(
                        PaymentCaptureProviderOutcome.Rejected,
                        SafeErrorCode: ProviderRejectionParser.SanitizeErrorCode(intent.Status))
                };
            }

            if (intent?.Error != null)
            {
                return new PaymentCaptureProviderResult(
                    StripeProviderOutcome.ToCapture(StripeOutcomeMapper.Map(intent.Error)),
                    SafeErrorCode: StripeOutcomeMapper.SafeCode(intent.Error));
            }

            if (ProviderRejectionParser.TryGetValidationErrorCode(error, out var safeErrorCode))
            {
                return new PaymentCaptureProviderResult(
                    PaymentCaptureProviderOutcome.Rejected,
                    SafeErrorCode: safeErrorCode);
            }

            _logger.LogWarning(
                "Payment capture returned no usable response Provider={Provider} HasPackageError={HasPackageError}",
                PaymentLogValue.Label(provider.ProviderName),
                !string.IsNullOrWhiteSpace(error));

            return new PaymentCaptureProviderResult(
                StripeUnavailable.IsTransient(error)
                    ? PaymentCaptureProviderOutcome.Unavailable
                    : PaymentCaptureProviderOutcome.OutcomeUnknown);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PaymentCaptureProviderResult(PaymentCaptureProviderOutcome.Timeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Payment capture provider call failed Provider={Provider} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return new PaymentCaptureProviderResult(
                PaymentCaptureProviderOutcome.OutcomeUnknown);
        }
    }

    /// <summary>
    /// What the returned intent state means for the capture.
    /// </summary>
    /// <remarks>
    /// A captured intent comes back already succeeded, and Stripe raises no event naming the
    /// capture, so it is settled here and now. <c>processing</c> belongs to payment methods
    /// that clear asynchronously; it is reported as submitted rather than settled, because the
    /// money has not moved yet and claiming otherwise would overstate what happened.
    /// </remarks>
    private static PaymentCaptureProviderOutcome ResolveOutcome(string? status) => status switch
    {
        "succeeded" => PaymentCaptureProviderOutcome.Settled,
        "processing" => PaymentCaptureProviderOutcome.Submitted,
        _ => PaymentCaptureProviderOutcome.Rejected
    };
}
