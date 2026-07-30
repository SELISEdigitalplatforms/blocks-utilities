using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>Opens a Stripe Checkout Session and returns its hosted page URL.</summary>
public sealed class StripeCheckoutSessionClient : IPaymentSessionClient
{
    private readonly IHttpService _httpService;
    private readonly StripeEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<StripeCheckoutSessionClient> _logger;

    public StripeCheckoutSessionClient(
        IHttpService httpService,
        StripeEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<StripeCheckoutSessionClient> logger)
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

    public async Task<ProviderSessionCreationResult> CreateSessionAsync(
        PaymentProvider provider,
        ProviderInitiationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);

        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl))
        {
            _logger.LogError(
                "Payment provider endpoint failed security validation Provider={Provider}",
                PaymentLogValue.Label(provider.ProviderName));

            return new ProviderSessionCreationResult
            {
                Outcome = ProviderClientOutcome.Unavailable
            };
        }

        var url = StripeUrl.Build(provider.ApiBaseUrl, "v1/checkout/sessions");
        var form = StripeInitiationRequestFactory.ReadForm(request);

        try
        {
            var (session, error) = await _httpService
                .SendFormUrlEncoded<StripeCheckoutSession>(
                    HttpMethod.Post,
                    form,
                    url,
                    StripeRequestHeaders.Create(provider, idempotencyKey),
                    cancellationToken,
                    Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

            if (session is { Id: not null, Url: not null })
            {
                return new ProviderSessionCreationResult
                {
                    Outcome = ProviderClientOutcome.Success,
                    Response = new HostedCheckoutSessionResponse
                    {
                        Id = session.Id,
                        Url = session.Url,
                        ExpiresAt = FromUnixSeconds(session.ExpiresAt)
                    }
                };
            }

            if (session?.Error != null)
            {
                _logger.LogWarning(
                    "Payment session request rejected Provider={Provider} ErrorType={ErrorType}",
                    PaymentLogValue.Label(provider.ProviderName),
                    PaymentLogValue.Label(session.Error.Type));

                return new ProviderSessionCreationResult
                {
                    Outcome = StripeOutcomeMapper.Map(session.Error),
                    ProviderErrorCode = StripeOutcomeMapper.SafeCode(session.Error)
                };
            }

            if (ProviderRejectionParser.TryGetValidationErrorCode(error, out var errorCode))
            {
                return new ProviderSessionCreationResult
                {
                    Outcome = ProviderClientOutcome.Rejected,
                    ProviderErrorCode = errorCode
                };
            }

            _logger.LogWarning(
                "Payment session request returned no usable response Provider={Provider} HasPackageError={HasPackageError}",
                PaymentLogValue.Label(provider.ProviderName),
                !string.IsNullOrWhiteSpace(error));

            return new ProviderSessionCreationResult
            {
                Outcome = StripeUnavailable.IsTransient(error)
                    ? ProviderClientOutcome.Unavailable
                    : ProviderClientOutcome.Failure
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Payment session request timed out Provider={Provider}",
                PaymentLogValue.Label(provider.ProviderName));

            return new ProviderSessionCreationResult
            {
                Outcome = ProviderClientOutcome.Timeout
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Payment session request failed Provider={Provider} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return new ProviderSessionCreationResult
            {
                Outcome = ProviderClientOutcome.Failure
            };
        }
    }

    private static DateTime? FromUnixSeconds(long? seconds) =>
        seconds.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value).UtcDateTime
            : null;
}
