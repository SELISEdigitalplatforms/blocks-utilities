import { Loader2 } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Label } from "@/components/ui-kits/label/label";
import { toast } from "@/hooks/use-toast";
import { useRunDueJobs } from "../hooks/use-run-due-jobs";
import type {
  SimulationWorkType,
  SubscriptionSimulationJobRunResponse,
} from "../models/subscription-simulation-harness.model";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const getWorkTypes = (): { value: SimulationWorkType; label: string }[] => [
  { value: "Renewal", label: tStatic("utilities.subscription-simulation", "RUN_DUE_JOBS_DIALOG.RENEWAL") },
  { value: "UsagePeriodClosure", label: tStatic("utilities.subscription-simulation", "RUN_DUE_JOBS_DIALOG.USAGE_PERIOD_CLOSURE") },
  { value: "UsageInvoiceCharge", label: tStatic("utilities.subscription-simulation", "RUN_DUE_JOBS_DIALOG.USAGE_INVOICE_CHARGE") },
  { value: "OutboxPublication", label: tStatic("utilities.subscription-simulation", "RUN_DUE_JOBS_DIALOG.OUTBOX_PUBLICATION") },
];

export const RunDueJobsDialog = ({
  subscriptionId,
  organizationId,
  open,
  onOpenChange,
  onResult,
}: {
  subscriptionId: string;
  organizationId: string | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onResult: (result: SubscriptionSimulationJobRunResponse) => void;
}) => {
  const { t } = useT("utilities.subscription-simulation");
  const { mutateAsync, isPending } = useRunDueJobs();

  const [selected, setSelected] = useState<Set<SimulationWorkType>>(new Set());
  const [formError, setFormError] = useState<string | null>(null);

  const toggle = (workType: SimulationWorkType, checked: boolean) => {
    setSelected((current) => {
      const next = new Set(current);
      if (checked) {
        next.add(workType);
      } else {
        next.delete(workType);
      }
      return next;
    });
  };

  const submit = async () => {
    setFormError(null);

    try {
      const result = await mutateAsync({
        subscriptionId,
        request: { organizationId, workTypes: Array.from(selected) },
      });

      onResult(result);

      toast({
        variant: "success",
        title: t("RUN_DUE_JOBS_DIALOG.DUE_JOBS_RUN"),
        description: t("RUN_DUE_JOBS_DIALOG.COMPLETED_NOT_DUE", { completed: result.completed, notDue: result.notDue }),
      });

      onOpenChange(false);
    } catch (error) {
      setFormError(
        error instanceof Error ? error.message : t("RUN_DUE_JOBS_DIALOG.THE_DUE_BACKGROUND_WORK_COULD"),
      );
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!isPending) {
          onOpenChange(next);
        }
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("RUN_DUE_JOBS_DIALOG.RUN_DUE_JOBS")}</DialogTitle>
          <DialogDescription>
            {t("RUN_DUE_JOBS_DIALOG.SENDS_RUNS_WHICHEVER_DUE", { endpoint: `POST /api/subscription-simulation/subscriptions/${subscriptionId}/jobs/run-due` })}</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-2">
            <Label>{t("RUN_DUE_JOBS_DIALOG.WORK_TYPES")}</Label>
            {getWorkTypes().map((workType) => (
              <div className="flex items-center gap-2" key={workType.value}>
                <Checkbox
                  id={`run-due-jobs-${workType.value}`}
                  checked={selected.has(workType.value)}
                  onCheckedChange={(checked) => toggle(workType.value, checked === true)}
                />
                <Label htmlFor={`run-due-jobs-${workType.value}`} className="font-normal">
                  {workType.label}
                </Label>
              </div>
            ))}
            <p className="text-xs text-muted-foreground">
              {t("RUN_DUE_JOBS_DIALOG.LEAVE_EVERYTHING_UNCHECKED_TO_RUN")}
            </p>
          </div>

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            {t("common:CANCEL")}
          </Button>
          <Button onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {t("RUN_DUE_JOBS_DIALOG.RUN_DUE_JOBS")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
