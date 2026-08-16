using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Charges a subscription's stored card off-session, through the existing recurring-payment
/// stack.
/// </summary>
/// <remarks>
/// Provider-neutral by construction, not Stripe-specific: <see
/// cref="SubscriptionChargeRequest.ProviderName"/> flows straight through to <see
/// cref="IRecurringPaymentService"/>, which already resolves the real charge gateway per
/// provider the same way the rest of the payment module does. Adding an Adyen subscriber needs
/// no new code here — only that tenant's <c>BillingAccount.ProviderName</c> naming Adyen.
/// <para>
/// This class is what a later Stripe Invoice integration replaces. Nothing upstream of <see
/// cref="ISubscriptionBillingGateway"/> changes when that happens.
/// </para>
/// </remarks>
public sealed class RecurringChargeBillingGateway : ISubscriptionBillingGateway
{
    private const string RecurringProcessingModel = "Subscription";

    private readonly IRecurringPaymentService _recurringPayments;
    private readonly ICurrencyMinorUnitResolver _currency;

    public RecurringChargeBillingGateway(
        IRecurringPaymentService recurringPayments,
        ICurrencyMinorUnitResolver currency)
    {
        _recurringPayments = recurringPayments;
        _currency = currency;
    }

    public async Task<SubscriptionOperationResult<string>> ChargeAsync(
        SubscriptionChargeRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_currency.TryConvertBack(request.AmountMinor, request.CurrencyCode, out var amount))
        {
            return SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.Unavailable,
                "subscription_currency_unsupported",
                "This currency is not configured for payments.",
                correlationId);
        }

        var result = await _recurringPayments.CreateRecurringPaymentAsync(
            new CreateRecurringPaymentRequest
            {
                ProviderName = request.ProviderName,
                StoredPaymentMethodId = request.StoredPaymentMethodId,
                Amount = amount,
                CurrencyCode = request.CurrencyCode,
                OrderId = request.OrderId,
                RecurringProcessingModel = RecurringProcessingModel,
                Description = request.Description
            },
            idempotencyKey,
            correlationId,
            cancellationToken);

        return result.IsSuccess && result.Payment is not null
            ? SubscriptionOperationResult<string>.Success(
                result.Payment.PaymentDetailId,
                correlationId)
            : SubscriptionOperationResult<string>.Failure(
                result.FailureKind,
                result.ErrorCode,
                result.ErrorMessage,
                correlationId);
    }
}
