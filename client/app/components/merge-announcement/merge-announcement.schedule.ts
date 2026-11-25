// Temporary hotfix: announces the merge of Logic, Data, Localization and Monitor into Blocks OS.
// One build covers the whole campaign; the phase is picked from the viewer's clock (UTC).

/** Switch-over and start of the 1-hour downtime: 28 Nov 2026, 18:00 UTC. */
export const MERGE_AT = Date.UTC(2026, 10, 28, 18, 0, 0);

/** Old addresses stop forwarding to Blocks OS: 20 Dec 2026, 18:00 UTC. */
export const FORWARDING_ENDS_AT = Date.UTC(2026, 11, 20, 18, 0, 0);

export type MergePhase = "before" | "after";

/** "before" until MERGE_AT, "after" until FORWARDING_ENDS_AT, then null (banner gone). */
export function getMergePhase(now: number): MergePhase | null {
  if (now < MERGE_AT) return "before";
  if (now < FORWARDING_ENDS_AT) return "after";
  return null;
}

// setTimeout overflows above 2^31-1 ms (~24.8 days); longer waits are re-checked in steps.
const MAX_TIMEOUT_MS = 2_147_483_647;

/** Milliseconds until the phase next changes, or null once the campaign is over. */
export function msUntilNextPhaseChange(now: number): number | null {
  const next = now < MERGE_AT ? MERGE_AT : now < FORWARDING_ENDS_AT ? FORWARDING_ENDS_AT : null;
  return next === null ? null : Math.min(next - now, MAX_TIMEOUT_MS);
}

/** How long ✕ hides the banner before it comes back. */
export const DISMISS_TTL_MS = 60 * 60 * 1000;

/** True while a dismissal stored at `dismissedAt` (epoch ms) still hides the banner. */
export function isDismissalActive(dismissedAt: number | null, now: number): boolean {
  return dismissedAt !== null && dismissedAt <= now && now - dismissedAt < DISMISS_TTL_MS;
}

export const dismissStorageKey = (phase: MergePhase) =>
  `blocks-merge-announcement-dismissed:${phase}`;
