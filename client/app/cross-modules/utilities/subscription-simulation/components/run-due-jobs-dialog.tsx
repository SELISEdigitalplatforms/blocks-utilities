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

const WORK_TYPES: { value: SimulationWorkType; label: string }[] = [
  { value: "Renewal", label: "Renewal" },
  { value: "UsagePeriodClosure", label: "Usage period closure" },
  { value: "UsageInvoiceCharge", label: "Usage invoice charge" },
  { value: "OutboxPublication", label: "Outbox publication" },
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
        title: "Due jobs run",
        description: `${result.completed} completed, ${result.notDue} not due.`,
      });

      onOpenChange(false);
    } catch (error) {
      setFormError(
        error instanceof Error ? error.message : "The due background work could not be run.",
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
          <DialogTitle>Run due jobs</DialogTitle>
          <DialogDescription>
            Sends{" "}
            <code>
              POST /api/subscription-simulation/subscriptions/{subscriptionId}/jobs/run-due
            </code>
            — runs whichever due background work exists for this one subscription right now.
            Never a tenant-wide sweep, and never a scripted outcome: a renewal or a usage-invoice
            charge run here goes to the real payment gateway.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-2">
            <Label>Work types</Label>
            {WORK_TYPES.map((workType) => (
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
              Leave everything unchecked to run every work type this endpoint knows about.
            </p>
          </div>

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            Cancel
          </Button>
          <Button onClick={submit} disabled={isPending}>
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Run due jobs
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
