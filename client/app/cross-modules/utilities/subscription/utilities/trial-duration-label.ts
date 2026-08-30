import type { TrialDurationKindName } from "../models/subscription-plan.model";

interface TrialDuration {
  trialDurationKind?: TrialDurationKindName | null;
  trialDurationCount?: number | null;
}

/**
 * A short label describing a plan's trial rule, for a badge or a one-line summary. Null when the
 * plan has no trial at all — including a plan whose `trialDurationKind` came back null from the
 * server, which is how "no trial" is normalized regardless of whether the plan predates duration
 * kinds. Not tied to a single plan shape: the builder's own draft and the server's stored plan
 * are different types that both carry these same two fields.
 */
export const describeTrialDuration = (plan: TrialDuration): string | null => {
  switch (plan.trialDurationKind) {
    case "Days":
      return plan.trialDurationCount ? `${plan.trialDurationCount}-day trial` : null;
    case "AnniversaryMonths":
      return plan.trialDurationCount ? `${plan.trialDurationCount}-month trial` : null;
    case "EndOfCalendarMonth":
      return "Trial until end of month";
    default:
      return null;
  }
};

/**
 * A full sentence describing when the trial ends and what happens then -- for the plan summary
 * card, where the badge's short label is not enough context.
 */
export const describeTrialSentence = (
  plan: TrialDuration & { trialRequiresPaymentMethod: boolean },
): string | null => {
  const span =
    plan.trialDurationKind === "Days" && plan.trialDurationCount
      ? `${plan.trialDurationCount} days`
      : plan.trialDurationKind === "AnniversaryMonths" && plan.trialDurationCount
        ? `${plan.trialDurationCount} month${plan.trialDurationCount === 1 ? "" : "s"}`
        : plan.trialDurationKind === "EndOfCalendarMonth"
          ? "until the end of the calendar month"
          : null;

  if (span === null) {
    return null;
  }

  const freeSpan = plan.trialDurationKind === "EndOfCalendarMonth" ? span : `for ${span}`;

  // Both branches describe a trial that is free until it ends -- a card being collected up front
  // is a condition of starting, not a charge. This read "Charged at signup" for the card-required
  // case until it was caught live in the plan builder's own preview: the third place this sentence
  // was written independently of step-trial.tsx (#355) and trial-duration-label.ts's own
  // card-required branch, and the only one still saying so after #353 made it false.
  return plan.trialRequiresPaymentMethod
    ? `A card is saved now, free ${freeSpan}, then the first charge is taken when the trial ends.`
    : `Free ${freeSpan}, then the first charge is taken.`;
};
