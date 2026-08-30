import { describe, expect, it } from "vitest";
import { describeTrialSentence } from "./trial-duration-label";

/**
 * The sentence that reads "Charged at signup" in production right now, on the plan builder's own
 * preview -- a third place saying this independently of step-trial.tsx (fixed in #355) and this
 * file's own card-free branch, which was already correct. Every trial is free until it ends since
 * #353; a card being collected up front is a condition of starting, not a charge.
 */
describe("describeTrialSentence", () => {
  it("does not claim a card-required trial is charged at signup", () => {
    const sentence = describeTrialSentence({
      trialDurationKind: "Days",
      trialDurationCount: 14,
      trialRequiresPaymentMethod: true,
    });

    expect(sentence).not.toMatch(/charged at signup/i);
    expect(sentence).toMatch(/first charge is taken when the trial ends/i);
    expect(sentence).toContain("14 days");
  });

  it("still describes a card-free trial as free until the first charge", () => {
    const sentence = describeTrialSentence({
      trialDurationKind: "Days",
      trialDurationCount: 14,
      trialRequiresPaymentMethod: false,
    });

    expect(sentence).toBe("Free for 14 days, then the first charge is taken.");
  });

  it("says a card is saved for the anniversary-month cadence too", () => {
    const sentence = describeTrialSentence({
      trialDurationKind: "AnniversaryMonths",
      trialDurationCount: 1,
      trialRequiresPaymentMethod: true,
    });

    expect(sentence).toContain("1 month");
    expect(sentence).toMatch(/card is saved now/i);
  });

  it("says a card is saved for the end-of-calendar-month cadence too", () => {
    const sentence = describeTrialSentence({
      trialDurationKind: "EndOfCalendarMonth",
      trialDurationCount: null,
      trialRequiresPaymentMethod: true,
    });

    expect(sentence).toBe(
      "A card is saved now, free until the end of the calendar month, then the first charge is " +
        "taken when the trial ends.",
    );
  });

  it("returns null for a plan with no trial", () => {
    expect(
      describeTrialSentence({
        trialDurationKind: null,
        trialDurationCount: null,
        trialRequiresPaymentMethod: true,
      }),
    ).toBeNull();
  });
});
