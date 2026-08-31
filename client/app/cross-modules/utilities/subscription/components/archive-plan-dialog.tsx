import { useEffect, useState } from "react";
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
          : "The plan could not be archived. Nothing has changed.",
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
            Archive {plan.displayName}?
          </DialogTitle>
          <DialogDescription>
            <span className="font-mono text-xs">{plan.code}</span>
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3 text-sm">
          <ul className="space-y-2">
            <li className="flex gap-2">
              <span aria-hidden="true">•</span>
              <span>
                It disappears from the plans your customers can choose, and from
                plan changes onto it.
              </span>
            </li>
            <li className="flex gap-2">
              <span aria-hidden="true">•</span>
              <span>
                New subscriptions and plan changes naming it will be{" "}
                <strong>refused</strong>.
              </span>
            </li>
            <li className="flex gap-2">
              <span aria-hidden="true">•</span>
              <span>
                Everyone already subscribed{" "}
                <strong>carries on unchanged</strong> — they bill from the terms
                they bought, so renewals, usage, entitlements and invoices are
                unaffected.
              </span>
            </li>
            <li className="flex gap-2">
              <span aria-hidden="true">•</span>
              <span>
                <strong>This cannot be undone.</strong> There is no way to put
                the plan back on sale.
              </span>
            </li>
            <li className="flex gap-2">
              <span aria-hidden="true">•</span>
              <span>
                You can still open it, and duplicate it later to build a
                replacement.
              </span>
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
            Keep it on sale
          </Button>
          <Button variant="destructive" onClick={archive} disabled={isArchiving}>
            {isArchiving ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                Archiving…
              </>
            ) : (
              "Archive permanently"
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
