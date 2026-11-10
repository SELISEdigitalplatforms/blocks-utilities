import { test } from "../../support/test-base";
import {
  activateAtContractPrice,
  advanceRenewalAndFindRecordDocument,
  chooseOrganization,
  openOfflineBilling,
  organizationState,
  repriceFromNextRenewal,
  verifyBilledByInvoice,
  verifyCustomerViewHidesCardPrompts,
} from "../../page/subscripptions/offline-billing";

/**
 * Spec 006 end to end: activate a bank transfer customer, re-price it, see the customer's view
 * offer no card, renew, and receive the record document. Runs against the organization
 * `E2E_OFFLINE_ORGANIZATION` names (or the first listed); it needs a complete billing profile, and
 * the signed-in user needs `subscription::manage`. An organization already offline is
 * carried on from where it stands, so the spec can be rerun against the same project.
 */
test.describe("flow: Subscriptions - Offline billing", () => {
  test("Offline billing - activate, re-price, badge without card prompts, renew, record document", async ({ page }) => {
    test.setTimeout(300_000);

    await openOfflineBilling(page);

    const chosen = await test.step("[Positive] an organization is chosen before anything is offered", () =>
      chooseOrganization(page));
    test.skip(!chosen, "the project has no organization to activate an offline subscription for");

    const state = await organizationState(page);
    test.skip(state === "card", "the organization already pays by card; switching is out of scope");

    if (state === "activatable") {
      const activated = await test.step("[Positive] activating at a contract price needs no payment", () =>
        activateAtContractPrice(page, "500"));
      test.skip(!activated, "the organization's billing profile is incomplete, so activation is refused");
    }

    await test.step("[Positive] the subscription is billed by invoice", () => verifyBilledByInvoice(page));
    await test.step("[Positive] re-pricing is scheduled from the next renewal", () =>
      repriceFromNextRenewal(page, "600"));
    await test.step("[Positive] the customer's view shows the badge and no card prompt", () =>
      verifyCustomerViewHidesCardPrompts(page));
    await test.step("[Positive] a renewal issues a record document without a charge", () =>
      advanceRenewalAndFindRecordDocument(page));
  });
});
