import { test } from "../../support/test-base";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";
import {
  openCreatePaymentForm,
  verifyFormLoadsWithDefaults,
  verifyOrganizationDropdownBehavior,
  verifyProviderAndCurrencyDropdownOptions,
  verifyProviderDropdownSwitches,
  verifyCurrencyDropdownAcceptsDifferent,
  verifyAmountAcceptsDecimal,
  verifyRememberCardSwitchToggle,
  verifyRecurringSwitchPermanentlyDisabled,
  verifyBlankOrderIdShowsRequired,
  verifyOrderIdOver80CharsShowsLengthError,
  verifyOrderIdCapsAt80Chars,
  verifyOrderIdWhitespaceTrimmedInRequest,
  verifyZeroAmountShowsGreaterThanZeroError,
  verifyAmountAboveLimitRejected,
  verifySubmitWithInvalidAmountDoesNotOpenCheckout,
  verifyCheckoutPreferencesDefaultOff,
  verifyNonHttpsRedirectUrlRejected,
  verifyApiErrorOnSubmit,
  verifySuccessfulSubmitOpensCheckout,
  verifyPopupBlockedFallbackShowsManualLink,
  verifySubmitButtonShowsLoadingState,
  verifySidePanelExplainsSecureRedirectFlow,
  verifyBackToPaymentsReturnsToList,
} from "../../page/payments/create-payment";

test.describe("flow: Payments — Create Payment", () => {
  test("Create Payment — defaults, validation, secure checkout flow, side panel", async ({
    page,
  }) => {
    test.setTimeout(180_000);
    await openUtilitiesDashboard(page);
    const form = await openCreatePaymentForm(page);

    await test.step("[Positive] form loads with sensible defaults", () =>
      verifyFormLoadsWithDefaults(form));
    await test.step("[Positive] Organization dropdown defaults to 'Use my current organization' and lists organizations when available", () =>
      verifyOrganizationDropdownBehavior(page, form));
    await test.step("[Positive] Provider and Currency dropdowns list the expected options", () =>
      verifyProviderAndCurrencyDropdownOptions(page, form));
    await test.step("[Positive] Provider dropdown switches between Adyen and Stripe", () =>
      verifyProviderDropdownSwitches(page, form));
    await test.step("[Positive] Currency dropdown accepts a different currency and the form keeps the rest of its state", () =>
      verifyCurrencyDropdownAcceptsDifferent(page, form));
    await test.step("[Positive] Amount field accepts a decimal value", () =>
      verifyAmountAcceptsDecimal(form));
    await test.step("[Positive] 'Offer to save payment method' switch toggles on and off", () =>
      verifyRememberCardSwitchToggle(form));
    await test.step("[Security] Recurring payment switch is permanently disabled", () =>
      verifyRecurringSwitchPermanentlyDisabled(form));
    await test.step("[Negative] blank Order ID shows 'Order ID is required.'", () =>
      verifyBlankOrderIdShowsRequired(page, form));
    await test.step("[Negative] Order ID over 80 characters shows the length error", () =>
      verifyOrderIdOver80CharsShowsLengthError(page, form));
    await test.step("[Positive] Order ID input caps at 80 characters", () =>
      verifyOrderIdCapsAt80Chars(form));
    await test.step("[Positive] Order ID whitespace is trimmed before submission (request payload)", () =>
      verifyOrderIdWhitespaceTrimmedInRequest(page, form));
    await test.step("[Negative] zero/negative amount shows 'Amount must be greater than zero.'", () =>
      verifyZeroAmountShowsGreaterThanZeroError(page, form));
    await test.step("[Negative] amount above the supported limit is rejected", () =>
      verifyAmountAboveLimitRejected(page, form));
    await test.step("[Negative] submitting with an invalid amount does not open a checkout tab", () =>
      verifySubmitWithInvalidAmountDoesNotOpenCheckout(page, form));
    await test.step("[Security] Checkout preferences default to off (no silent card-saving or recurring charges)", () =>
      verifyCheckoutPreferencesDefaultOff(form));
    await test.step("[Negative] non-https redirect URL is rejected with an error banner", () =>
      verifyNonHttpsRedirectUrlRejected(page, form));
    await test.step("[Negative] API error on submit shows the error banner", () =>
      verifyApiErrorOnSubmit(page, form));
    await test.step("[Positive] successful submit opens the checkout in a new tab and shows the success card", () =>
      verifySuccessfulSubmitOpensCheckout(page, form));
    await test.step("[Positive] popup-blocked fallback exposes a manual 'Open checkout' link", () =>
      verifyPopupBlockedFallbackShowsManualLink(page, form));
    await test.step("[Positive] submit button shows a loading state while the create-payment request is in flight", () =>
      verifySubmitButtonShowsLoadingState(page, form));
    await test.step("[Positive] side panel explains the secure redirect flow", () =>
      verifySidePanelExplainsSecureRedirectFlow(page));
    await test.step("[Positive] 'Back to payments' returns to the payment list", () =>
      verifyBackToPaymentsReturnsToList(page));
  });
});
