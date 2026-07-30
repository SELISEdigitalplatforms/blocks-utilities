using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Removes a saved card by detaching the PaymentMethod from its customer.
/// </summary>
/// <remarks>
/// Stripe has no delete for payment methods: detaching leaves the object in place but unusable
/// for new charges, which is the operation this maps to. Detaching needs neither the merchant
/// nor the shopper, because the method's customer is already recorded against it — so unlike
/// the Adyen gateway, nothing from <see cref="StoredPaymentMethod"/> reaches the wire.
/// </remarks>
public sealed class StripeStoredPaymentMethodProviderGateway :
    IStoredPaymentMethodProviderGateway
{
    private readonly IHttpService _httpService;
    private readonly StripeEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<StripeStoredPaymentMethodProviderGateway> _logger;

    public StripeStoredPaymentMethodProviderGateway(
        IHttpService httpService,
        StripeEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<StripeStoredPaymentMethodProviderGateway> logger)
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

    public async Task<StoredPaymentMethodRemovalOutcome> RemoveAsync(
        PaymentProvider provider,
        StoredPaymentMethod method,
        string providerToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(providerToken))
        {
            return StoredPaymentMethodRemovalOutcome.OperationalFailure;
        }

        var url = StripeUrl.Build(
            provider.ApiBaseUrl,
            $"v1/payment_methods/{Uri.EscapeDataString(providerToken)}/detach");

        try
        {
            var (detached, error) = await _httpService
                .SendFormUrlEncoded<StripePaymentMethod>(
                    HttpMethod.Post,
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    url,
                    // Detaching is naturally idempotent — a second call reports the method is
                    // no longer attached — so it carries no idempotency key to consume.
                    StripeRequestHeaders.Read(provider),
                    cancellationToken,
                    Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

            if (detached is { Id: not null, Error: null })
            {
                return StoredPaymentMethodRemovalOutcome.Removed;
            }

            if (detached?.Error != null)
            {
                return ClassifyStripeError(provider, detached.Error);
            }

            if (IsAlreadyRemoved(error))
            {
                return StoredPaymentMethodRemovalOutcome.Removed;
            }

            _logger.LogWarning(
                "Stored payment method provider removal returned an unusable response Provider={Provider} HasPackageError={HasPackageError}",
                PaymentLogValue.Label(provider.ProviderName),
                !string.IsNullOrWhiteSpace(error));

            return IsDefinitiveOperationalFailure(error)
                ? StoredPaymentMethodRemovalOutcome.OperationalFailure
                : StoredPaymentMethodRemovalOutcome.OutcomeUnknown;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StoredPaymentMethodRemovalOutcome.OutcomeUnknown;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Stored payment method provider removal failed Provider={Provider} ExceptionType={ExceptionType}",
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return StoredPaymentMethodRemovalOutcome.OutcomeUnknown;
        }
    }

    /// <summary>
    /// A method that is missing or already unattached is the state removal was asking for, so
    /// it counts as removed rather than as a failure to retry.
    /// </summary>
    private StoredPaymentMethodRemovalOutcome ClassifyStripeError(
        PaymentProvider provider,
        StripeError error)
    {
        if (string.Equals(error.Code, "resource_missing", StringComparison.Ordinal) ||
            IsNotAttached(error.Message))
        {
            return StoredPaymentMethodRemovalOutcome.Removed;
        }

        _logger.LogWarning(
            "Stored payment method provider removal was rejected Provider={Provider} ErrorType={ErrorType}",
            PaymentLogValue.Label(provider.ProviderName),
            PaymentLogValue.Label(error.Type));

        return string.Equals(error.Type, "authentication_error", StringComparison.Ordinal)
            ? StoredPaymentMethodRemovalOutcome.OperationalFailure
            : StoredPaymentMethodRemovalOutcome.OutcomeUnknown;
    }

    private static bool IsNotAttached(string? message) =>
        message?.Contains("not attached", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsAlreadyRemoved(string? error) =>
        error?.Contains("404", StringComparison.OrdinalIgnoreCase) == true ||
        error?.Contains("resource_missing", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsDefinitiveOperationalFailure(string? error) =>
        error?.Contains("401", StringComparison.OrdinalIgnoreCase) == true ||
        error?.Contains("403", StringComparison.OrdinalIgnoreCase) == true;
}
