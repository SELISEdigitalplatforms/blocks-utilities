using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class CheckoutCallbackContextResolver : ICheckoutCallbackContextResolver
{
    private readonly ICheckoutCallbackStateProtector _stateProtector;
    private readonly IPaymentRepository _repository;
    private readonly IPaymentProviderCache _providerCache;
    private readonly ICheckoutUrlPolicy _checkoutUrlPolicy;

    public CheckoutCallbackContextResolver(
        ICheckoutCallbackStateProtector stateProtector,
        IPaymentRepository repository,
        IPaymentProviderCache providerCache,
        ICheckoutUrlPolicy checkoutUrlPolicy)
    {
        _stateProtector = stateProtector;
        _repository = repository;
        _providerCache = providerCache;
        _checkoutUrlPolicy = checkoutUrlPolicy;
    }

    public async Task<CheckoutCallbackContextResolution> ResolveAsync(
        string protectedState,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!_stateProtector.TryRead(protectedState, out var untrustedState))
        {
            return InvalidState();
        }

        var provider = await GetProviderAsync(untrustedState, cancellationToken);

        if (provider == null || !provider.IsEnabled)
        {
            return PaymentNotFound();
        }

        if (!TryVerifyState(protectedState, provider, out var verifiedState))
        {
            return InvalidState();
        }

        var payment = await _repository.GetByIdAsync(
            verifiedState.TenantId,
            verifiedState.PaymentDetailId,
            cancellationToken);

        var validationFailure = ValidatePayment(payment, verifiedState, sessionId);

        return validationFailure ?? CheckoutCallbackContextResolution.Success(
            new CheckoutCallbackContext(verifiedState, provider, payment!));
    }

    private Task<PaymentProvider?> GetProviderAsync(
        CheckoutCallbackState state,
        CancellationToken cancellationToken) =>
        _providerCache.GetAsync(
            state.TenantId,
            state.ProviderName,
            () => _repository.GetProviderAsync(
                state.TenantId,
                state.ProviderName,
                cancellationToken));

    private bool TryVerifyState(
        string protectedState,
        PaymentProvider provider,
        out CheckoutCallbackState verifiedState) =>
        _stateProtector.TryUnprotect(
            protectedState,
            provider.ReturnStateHmacKey ?? string.Empty,
            provider.PreviousReturnStateHmacKey,
            out verifiedState);

    private CheckoutCallbackContextResolution? ValidatePayment(
        PaymentDetail? payment,
        CheckoutCallbackState state,
        string sessionId)
    {
        if (payment == null ||
            !string.Equals(payment.ProviderName, state.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return PaymentNotFound();
        }

        if (!HasValidNonce(payment, state.Nonce))
        {
            return InvalidState();
        }

        if (!string.Equals(payment.SessionId, sessionId, StringComparison.Ordinal))
        {
            return CheckoutCallbackContextResolution.Failed(
                CheckoutCallbackResult.Failure(
                    PaymentFailureKind.Validation,
                    "session_mismatch",
                    "The payment session is invalid."));
        }

        if (string.IsNullOrWhiteSpace(payment.FrontendResultUrlSnapshot) ||
            !_checkoutUrlPolicy.IsAllowedFrontendResultUrl(payment.FrontendResultUrlSnapshot))
        {
            return PaymentNotFound();
        }

        return null;
    }

    private static bool HasValidNonce(PaymentDetail payment, string nonce) =>
        !string.IsNullOrWhiteSpace(payment.ReturnStateNonceHash) &&
        PaymentHashing.RequestHashesMatch(
            payment.ReturnStateNonceHash,
            PaymentHashing.HashSensitiveValue(nonce));

    private static CheckoutCallbackContextResolution InvalidState() =>
        CheckoutCallbackContextResolution.Failed(
            CheckoutCallbackResult.Failure(
                PaymentFailureKind.Validation,
                "invalid_return_state",
                "The checkout callback state is invalid or expired."));

    private static CheckoutCallbackContextResolution PaymentNotFound() =>
        CheckoutCallbackContextResolution.Failed(
            CheckoutCallbackResult.Failure(
                PaymentFailureKind.NotFound,
                "payment_not_found",
                "The payment was not found."));
}
