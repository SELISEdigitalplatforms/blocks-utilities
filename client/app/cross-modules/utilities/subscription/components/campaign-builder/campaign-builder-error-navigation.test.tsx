import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/**
 * A save that cannot proceed takes the author to the step that is wrong.
 *
 * Reaching this at all takes some doing, and it is worth being precise about how. `Next` is
 * disabled while the current step has problems, and stepping back through the progress bar clears
 * the completed steps behind it — so an author cannot simply break a field and walk forward to
 * Save. What can happen is the `plans` prop changing underneath a review step that is already
 * open: a free-opening campaign names the entitlement it caps, and that name is checked against
 * the plan it applies to. If the plan is edited elsewhere while this builder sits on Review, a step
 * the author already passed becomes invalid.
 *
 * Before this change Save then returned in silence — no message, no movement, nothing — and the
 * per-step problem list that would have explained it is hidden on the last step, the only step Save
 * appears on.
 */

beforeAll(() => {
  Element.prototype.hasPointerCapture ??= vi.fn(() => false) as never;
  Element.prototype.setPointerCapture ??= vi.fn() as never;
  Element.prototype.releasePointerCapture ??= vi.fn() as never;
  Element.prototype.scrollIntoView ??= vi.fn() as never;
});

const toast = vi.fn();

vi.mock("@/hooks/use-toast", () => ({ toast: (...args: unknown[]) => toast(...args) }));

// Stubbed so this test is about where the author is sent, not about any one control. The stepper
// shell and the submit path are real.
vi.mock("./step-identity", () => ({ StepIdentity: () => <div data-testid="step-body" /> }));
vi.mock("./step-benefit", () => ({ StepBenefit: () => <div data-testid="step-body" /> }));
vi.mock("./step-eligibility", () => ({ StepEligibility: () => <div data-testid="step-body" /> }));
vi.mock("./step-review", () => ({ StepReview: () => <div data-testid="step-body" /> }));

import type { SubscriptionPlan } from "../../models/subscription-plan.model";
import { CampaignBuilder } from "./campaign-builder";
import { EMPTY_DRAFT, withCampaignKind } from "./campaign-draft";
import type { CampaignDraft } from "./campaign-draft";

const onSubmit = vi.fn(async () => {});

const planWith = (entitlementKeys: string[]) =>
  ({
    planId: "plan-1",
    code: "pro",
    displayName: "Pro",
    prices: [{ priceId: "price-1", currencyCode: "CHF", interval: "Month", intervalCount: 1 }],
    entitlements: entitlementKeys.map((key) => ({ key, limitKind: "Count" })),
    meters: [],
    quantityItems: [],
  }) as unknown as SubscriptionPlan;

/** A free-opening campaign with nothing wrong with it, given a plan that has its entitlement. */
const validDraft: CampaignDraft = withCampaignKind(
  {
    ...EMPTY_DRAFT,
    code: "launch-25",
    displayName: "Launch offer",
    priceIds: ["price-1"],
    planCodes: ["pro"],
    validFromDate: "2026-09-01",
    timeZoneId: "Europe/Zurich",
    entitlementKey: "screening",
    entitlementLimit: "50",
  },
  "FreeOpeningCalendarPeriod",
);

const renderBuilder = (plans: SubscriptionPlan[]) =>
  render(
    <CampaignBuilder
      plans={plans}
      organizationId={undefined}
      isSubmitting={false}
      submissionError={null}
      onSubmit={onSubmit}
      onCancel={vi.fn()}
      initialDraft={validDraft}
    />,
  );

const walkToReview = async (user: ReturnType<typeof userEvent.setup>) => {
  for (let step = 1; step < 4; step++) {
    await user.click(screen.getByRole("button", { name: /next/i }));
  }
};

const save = () => screen.getByRole("button", { name: /create discount/i });

beforeEach(() => {
  vi.clearAllMocks();
});

describe("the campaign builder's review step", () => {
  it("saves a draft with nothing wrong with it", async () => {
    const user = userEvent.setup();
    renderBuilder([planWith(["screening"])]);

    await walkToReview(user);
    await user.click(save());

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(toast).not.toHaveBeenCalled();
  });

  it("says something when a step it already passed has gone invalid", async () => {
    const user = userEvent.setup();
    const view = renderBuilder([planWith(["screening"])]);

    await walkToReview(user);

    // The plan is edited elsewhere and its entitlement is gone, so eligibility no longer holds.
    view.rerender(
      <CampaignBuilder
        plans={[planWith(["something-else"])]}
        organizationId={undefined}
        isSubmitting={false}
        submissionError={null}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
        initialDraft={validDraft}
      />,
    );

    await user.click(save());

    await waitFor(() => expect(toast).toHaveBeenCalledTimes(1));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("names the step it sent the author to, and goes there", async () => {
    const user = userEvent.setup();
    const view = renderBuilder([planWith(["screening"])]);

    await walkToReview(user);

    view.rerender(
      <CampaignBuilder
        plans={[planWith(["something-else"])]}
        organizationId={undefined}
        isSubmitting={false}
        submissionError={null}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
        initialDraft={validDraft}
      />,
    );

    await user.click(save());

    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({
          variant: "destructive",
          title: expect.stringContaining("Eligibility"),
        }),
      ),
    );

    // Off the review step, so the step's own problem list is on screen to explain it.
    expect(screen.queryByRole("button", { name: /create discount/i })).toBeNull();
  });
});
