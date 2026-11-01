import { useEffect, useState } from "react";
import { useT } from "@seliseblocks/genesis-os";
import { AlertTriangle, Loader2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { RichText } from "./rich-text";

/**
 * Confirms archiving one plan.
 *
 * Every consequence is stated rather than summarised, because they are not obvious from the word
 * "archive" and they are not symmetrical: two of them are reassuring, one is irreversible. An
 * author who reads only the heading should still not be able to lose anything they did not expect
 * to lose.
 */
export const ArchivePlanDialog = ({
  plan,
  isOpen,
  onOpenChange,
  onConfirm,
}: {
  plan: SubscriptionPlan | null;
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
  /** Resolves when the plan is archived; rejects with a message worth showing. */
  onConfirm: (plan: SubscriptionPlan) => Promise<void>;
}) => {
  const { t } = useT("utilities.subscription");
  const [isArchiving, setIsArchiving] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  // A dialog reopened after a failure must not still be showing the last one, and one reopened for
  // a different plan certainly must not.
  useEffect(() => {
    if (isOpen) {
      setFailure(null);
    }
  }, [isOpen, plan?.planId]);

  if (!plan) {
    return null;
  }

  const archive = async () => {
    // The guard, not just the disabled attribute: a second Enter keypress arrives before React has
    // re-rendered the button, and archiving is irreversible enough to be worth defending twice.
    if (isArchiving) {
      return;
    }

    setIsArchiving(true);
    setFailure(null);

    try {
      await onConfirm(plan);
      onOpenChange(false);
    } catch (error) {
      setFailure(
        error instanceof Error
          ? error.message
          : t("ARCHIVE_PLAN_DIALOG.FAILURE"),
      );
    } finally {
      setIsArchiving(false);
    }
  };

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(open) => {
        // Closing mid-request would leave the caller unable to report what happened, and the
        // request cannot be recalled anyway.
        if (isArchiving) {
          return;
        }

        onOpenChange(open);
      }}
    >
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <span className="rounded-full bg-destructive/10 p-1.5 text-destructive">
              <AlertTriangle className="h-4 w-4" />
            </span>
            {/* The name and code both, because two plans in a family often differ only by code. */}
            {t("ARCHIVE_PLAN_DIALOG.TITLE", { name: plan.displayName })}
          </DialogTitle>
          <DialogDescription>
            <span className="font-mono text-xs">{plan.code}</span>
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3 text-sm">
          <ul className="space-y-2">
            <li className="flex gap-2">
              <span aria-hidden="true">•</span>
              <span>{t("ARCHIVE_PLAN_DIALOG.POINT_HIDDEN")}</span>
            </li>
            <li className="flex gap-2">
              <span aria-hidden="true">•</span>
              <span>
                <RichText text={t("ARCHIVE_PLAN_DIALOG.POINT_REFUSED")} />
              </span>
            </li>
            <li className="flex gap-2">
              <span aria-hidden="true">•</span>
              <span>
                <RichText text={t("ARCHIVE_PLAN_DIALOG.POINT_SUBSCRIBERS")} />
              </span>
            </li>
            <li className="flex gap-2">
              <span aria-hidden="true">•</span>
              <span>
                <RichText text={t("ARCHIVE_PLAN_DIALOG.POINT_IRREVERSIBLE")} />
              </span>
            </li>
            <li className="flex gap-2">
              <span aria-hidden="true">•</span>
              <span>{t("ARCHIVE_PLAN_DIALOG.POINT_DUPLICATE")}</span>
            </li>
          </ul>

          {failure ? (
            <p
              role="alert"
              className="rounded-md bg-destructive/10 p-3 text-sm text-destructive"
            >
              {failure}
            </p>
          ) : null}
        </div>

        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={isArchiving}
          >
            {t("ARCHIVE_PLAN_DIALOG.KEEP")}
          </Button>
          <Button variant="destructive" onClick={archive} disabled={isArchiving}>
            {isArchiving ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                {t("ARCHIVE_PLAN_DIALOG.ARCHIVING")}
              </>
            ) : (
              t("ARCHIVE_PLAN_DIALOG.CONFIRM")
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
