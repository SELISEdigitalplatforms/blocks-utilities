import { tSubscription } from "./subscription-i18n";
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
      return plan.trialDurationCount
        ? tSubscription("TRIAL_DURATION_LABEL.DAY_TRIAL", { count: plan.trialDurationCount })
        : null;
    case "AnniversaryMonths":
      return plan.trialDurationCount
        ? tSubscription("TRIAL_DURATION_LABEL.MONTH_TRIAL", { count: plan.trialDurationCount })
        : null;
    case "EndOfCalendarMonth":
      return tSubscription("TRIAL_DURATION_LABEL.UNTIL_END_OF_MONTH");
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
  const count = plan.trialDurationCount ?? 0;
  let span: string | null = null;

  if (plan.trialDurationKind === "Days" && count) {
    span = tSubscription("TRIAL_DURATION_LABEL.SPAN_DAYS", { count });
  } else if (plan.trialDurationKind === "AnniversaryMonths" && count) {
    span = tSubscription("TRIAL_DURATION_LABEL.SPAN_MONTHS", { count });
  } else if (plan.trialDurationKind === "EndOfCalendarMonth") {
    span = tSubscription("TRIAL_DURATION_LABEL.SPAN_END_OF_MONTH");
  }

  if (span === null) {
    return null;
  }

  const freeSpan =
    plan.trialDurationKind === "EndOfCalendarMonth"
      ? span
      : tSubscription("TRIAL_DURATION_LABEL.FOR_SPAN", { span });

  // Both branches describe a trial that is free until it ends -- a card being collected up front
  // is a condition of starting, not a charge. This read "Charged at signup" for the card-required
  // case until it was caught live in the plan builder's own preview: the third place this sentence
  // was written independently of step-trial.tsx (#355) and trial-duration-label.ts's own
  // card-required branch, and the only one still saying so after #353 made it false.
  return plan.trialRequiresPaymentMethod
    ? tSubscription("TRIAL_DURATION_LABEL.SENTENCE_CARD_REQUIRED", { span: freeSpan })
    : tSubscription("TRIAL_DURATION_LABEL.SENTENCE_NO_CARD", { span: freeSpan });
};
