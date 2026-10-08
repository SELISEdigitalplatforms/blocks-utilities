namespace Payment.DomainService.Providers;

/// <param name="MerchantConfiguration">
/// A <see cref="StoredPaymentChargeOutcome.Rejected"/> that came from the merchant's own provider
/// setup rather than from the card, such as Adyen's <c>905_1</c>. Carried apart so the payer is not
/// told their card declined and sent to try another one, which cannot help.
/// </param>
public sealed record StoredPaymentChargeProviderResult(
    StoredPaymentChargeOutcome Outcome,
    string? PspReference = null,
    string? ResultCode = null,
    string? SafeErrorCode = null,
    bool MerchantConfiguration = false);
