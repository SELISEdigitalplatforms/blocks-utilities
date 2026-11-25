import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { useAuthStore, useT } from "@seliseblocks/genesis-os";
import { Clock, Info, Link2, X } from "lucide-react";
import { useEffect, useLayoutEffect, useRef, useState } from "react";
import "./merge-announcement.css";
import {
  DISMISS_TTL_MS,
  dismissStorageKey,
  isDismissalActive,
  getMergePhase,
  msUntilNextPhaseChange,
  type MergePhase,
} from "./merge-announcement.schedule";

// Temporary hotfix: announces the merge of Logic, Data, Localization and Monitor
// into Blocks OS. Shown after login, above every layout; it switches from the "before" to the
// "after" text at 28 Nov 2026 18:00 UTC and disappears at 20 Dec 2026 18:00 UTC.

const PHASES: readonly MergePhase[] = ["before", "after"];

function readDismissedAt(phase: MergePhase): number | null {
  try {
    const value = Number(globalThis.localStorage?.getItem(dismissStorageKey(phase)));
    return Number.isFinite(value) && value > 0 ? value : null;
  } catch {
    return null;
  }
}

function writeDismissedAt(phase: MergePhase, at: number | null) {
  try {
    if (at === null) globalThis.localStorage?.removeItem(dismissStorageKey(phase));
    else globalThis.localStorage?.setItem(dismissStorageKey(phase), String(at));
  } catch {
    // Storage blocked (private mode etc.): the dismissal only lasts for this page view.
  }
}

/**
 * ✕ hides the banner for DISMISS_TTL_MS (it then comes back, also in an open tab).
 * Any login or logout clears it, so the banner shows again after every login. A page reload
 * keeps it: genesis-os restores isAuthenticated synchronously from localStorage.
 */
function useDismissal(phase: MergePhase | null, isAuthenticated: boolean) {
  const [now, setNow] = useState(() => Date.now());
  const [dismissedAt, setDismissedAt] = useState(
    () =>
      Object.fromEntries(PHASES.map((p) => [p, readDismissedAt(p)])) as Record<
        MergePhase,
        number | null
      >,
  );

  const previousAuth = useRef(isAuthenticated);
  useEffect(() => {
    if (previousAuth.current === isAuthenticated) return;
    previousAuth.current = isAuthenticated;
    PHASES.forEach((p) => writeDismissedAt(p, null));
    setDismissedAt({ before: null, after: null });
  }, [isAuthenticated]);

  const at = phase === null ? null : dismissedAt[phase];
  const dismissed = isDismissalActive(at, now);

  useEffect(() => {
    if (!dismissed || at === null) return;
    const timer = setTimeout(() => setNow(Date.now()), at + DISMISS_TTL_MS - Date.now() + 500);
    return () => clearTimeout(timer);
  }, [dismissed, at]);

  const dismiss = () => {
    if (phase === null) return;
    const stamp = Date.now();
    writeDismissedAt(phase, stamp);
    setDismissedAt((current) => ({ ...current, [phase]: stamp }));
    setNow(stamp);
  };

  return { dismissed, dismiss };
}

/** Re-renders when the phase changes, so an open tab moves from "before" to "after" on its own. */
function useMergePhase(): MergePhase | null {
  const [phase, setPhase] = useState(() => getMergePhase(Date.now()));
  useEffect(() => {
    const wait = msUntilNextPhaseChange(Date.now());
    if (wait === null) return;
    const timer = setTimeout(() => setPhase(getMergePhase(Date.now())), wait + 1000);
    return () => clearTimeout(timer);
  }, [phase]);
  return phase;
}

export function MergeAnnouncementBanner() {
  const { t } = useT("utilities.shared");
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const phase = useMergePhase();
  const { dismissed, dismiss } = useDismissal(phase, isAuthenticated);
  const [detailsOpen, setDetailsOpen] = useState(false);
  const bannerRef = useRef<HTMLDivElement>(null);

  const visible = isAuthenticated && phase !== null && !dismissed;

  // Tell the layout fix-ups in merge-announcement.css how tall the banner is.
  useLayoutEffect(() => {
    const root = document.documentElement;
    const banner = bannerRef.current;
    if (!visible || !banner) return;
    const update = () =>
      root.style.setProperty("--merge-banner-height", `${banner.getBoundingClientRect().height}px`);
    update();
    root.setAttribute("data-merge-banner", "");
    const observer = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(update);
    observer?.observe(banner);
    return () => {
      observer?.disconnect();
      root.removeAttribute("data-merge-banner");
      root.style.removeProperty("--merge-banner-height");
    };
  }, [visible]);

  if (!visible || phase === null) return null;

  const learnMore = (
    <button
      type="button"
      onClick={() => setDetailsOpen(true)}
      className="font-semibold underline underline-offset-[3px] hover:text-[#0b2650] dark:hover:text-white"
    >
      {t("MERGE_ANNOUNCEMENT.LEARN_MORE")}
    </button>
  );

  return (
    <>
      <div
        ref={bannerRef}
        role="region"
        aria-label={t("MERGE_ANNOUNCEMENT.REGION_LABEL")}
        data-testid="merge-announcement-banner"
        className="sticky top-0 z-[45] flex items-start gap-3 border-b border-[#c5d8f2] bg-[#e8f0fb] py-2.5 pl-4 pr-1.5 text-sm leading-5 text-[#12386b] sm:items-center sm:pl-6 sm:pr-4 dark:border-[#24456f] dark:bg-[#0f2440] dark:text-[#cfe0f7]"
      >
        <Info
          aria-hidden="true"
          className="mt-px h-[18px] w-[18px] shrink-0 text-[#1d4f9e] sm:mt-0 dark:text-[#8db7ec]"
        />
        <p className="m-0 flex-1">
          <span className="hidden sm:inline">
            <strong className="font-semibold">
              {t(
                phase === "before"
                  ? "MERGE_ANNOUNCEMENT.BEFORE_LEAD"
                  : "MERGE_ANNOUNCEMENT.AFTER_LEAD",
              )}
            </strong>{" "}
            {t(
              phase === "before"
                ? "MERGE_ANNOUNCEMENT.BEFORE_TEXT"
                : "MERGE_ANNOUNCEMENT.AFTER_TEXT",
            )}
          </span>
          <span className="sm:hidden">
            {t(
              phase === "before"
                ? "MERGE_ANNOUNCEMENT.BEFORE_SHORT"
                : "MERGE_ANNOUNCEMENT.AFTER_SHORT",
            )}
          </span>{" "}
          {learnMore}
        </p>
        <button
          type="button"
          aria-label={t("MERGE_ANNOUNCEMENT.DISMISS")}
          onClick={dismiss}
          className="-my-3 flex h-11 w-11 shrink-0 items-center justify-center rounded-md hover:bg-[#d6e5f8] sm:my-0 sm:h-9 sm:w-9 dark:hover:bg-[#1a3558]"
        >
          <X aria-hidden="true" className="h-[18px] w-[18px]" />
        </button>
      </div>

      <Dialog open={detailsOpen} onOpenChange={setDetailsOpen}>
        <DialogContent className="max-h-[calc(100dvh-2rem)] w-[calc(100%-2rem)] max-w-[560px] gap-[18px] overflow-y-auto rounded-xl p-7">
          <MergeDetails phase={phase} />
          <DialogFooter className="items-center gap-3 sm:justify-between">
            <p className="m-0 text-[13px] text-muted-foreground">
              {t("MERGE_ANNOUNCEMENT.DETAILS_CONTACT")}
            </p>
            <Button
              type="button"
              onClick={() => setDetailsOpen(false)}
              className="bg-[#1d4f9e] text-white hover:bg-[#12386b]"
            >
              {t("MERGE_ANNOUNCEMENT.DETAILS_GOT_IT")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

const strongClass = "font-semibold text-foreground";
const noticeClass =
  "flex items-start gap-2.5 rounded-[10px] border border-[#fde68a] bg-[#fffbeb] px-3.5 py-3 text-sm leading-[21px] text-[#78350f] dark:border-[#78350f] dark:bg-[#2a1d06] dark:text-[#fde68a]";

/** "Learn more" content. Bold leads and their sentences are separate keys (no rich-text i18n here). */
function MergeDetails({ phase }: { phase: MergePhase }) {
  const { t } = useT("utilities.shared");
  const k = (key: string) => t(`MERGE_ANNOUNCEMENT.${key}`);
  const before = phase === "before";
  const items: [lead: string, text: string][] = [
    ["BEFORE_FEATURES_LEAD", before ? "BEFORE_FEATURES_TEXT" : "AFTER_FEATURES_TEXT"],
    ["BEFORE_SWITCHER_LEAD", before ? "BEFORE_SWITCHER_TEXT" : "AFTER_SWITCHER_TEXT"],
    ["BEFORE_MIGRATE_LEAD", before ? "BEFORE_MIGRATE_TEXT" : "AFTER_MIGRATE_TEXT"],
  ];
  if (before) items.push(["BEFORE_LINKS_LEAD", "BEFORE_LINKS_TEXT"]);

  return (
    <>
      <DialogHeader className="pr-6 text-left">
        <DialogTitle className="text-[19px] leading-[26px]">
          {k(before ? "BEFORE_TITLE" : "AFTER_TITLE")}
        </DialogTitle>
        <DialogDescription className="text-sm leading-[22px]">
          {k(before ? "BEFORE_INTRO" : "AFTER_INTRO")}
        </DialogDescription>
      </DialogHeader>
      <div className="flex flex-col gap-2.5">
        <h3 className="m-0 text-[13px] font-semibold uppercase tracking-[0.04em] text-muted-foreground">
          {k("DETAILS_HEADING")}
        </h3>
        <ul className="m-0 flex list-disc flex-col gap-2 pl-5 text-sm leading-[21px] text-muted-foreground">
          {items.map(([lead, text]) => (
            <li key={lead}>
              <strong className={strongClass}>{k(lead)}</strong> {k(text)}
            </li>
          ))}
        </ul>
      </div>
      <div className={noticeClass}>
        {before ? (
          <Clock aria-hidden="true" className="mt-0.5 h-[18px] w-[18px] shrink-0" />
        ) : (
          <Link2 aria-hidden="true" className="mt-0.5 h-[18px] w-[18px] shrink-0" />
        )}
        <p className="m-0">
          <strong className="font-semibold">
            {k(before ? "BEFORE_DOWNTIME_LEAD" : "AFTER_LINKS_LEAD")}
          </strong>{" "}
          {k(before ? "BEFORE_DOWNTIME_TEXT" : "AFTER_LINKS_TEXT")}
        </p>
      </div>
    </>
  );
}
