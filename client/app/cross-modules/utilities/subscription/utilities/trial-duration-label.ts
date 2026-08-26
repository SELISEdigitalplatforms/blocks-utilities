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
 * A full sentence describing when the trial ends and what happens then — for the plan summary
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

  return plan.trialRequiresPaymentMethod
    ? `Charged at signup; the trial (${span}) governs the allowances, not the price.`
    : `Free ${plan.trialDurationKind === "EndOfCalendarMonth" ? span : `for ${span}`}, then the first charge is taken.`;
};
