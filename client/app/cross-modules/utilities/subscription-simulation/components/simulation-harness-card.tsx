import { Beaker } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import type {
  SubscriptionSimulationActionResponse,
  SubscriptionSimulationJobRunResponse,
} from "../models/subscription-simulation-harness.model";
import { useT } from "@seliseblocks/genesis-os";

type HarnessResult = SubscriptionSimulationActionResponse | SubscriptionSimulationJobRunResponse;

const isJobRun = (result: HarnessResult): result is SubscriptionSimulationJobRunResponse =>
  "jobs" in result;

const formatDate = (isoDate: string | null) =>
  isoDate ? new Date(isoDate).toLocaleString() : "—";

/**
 * Test-harness actions, kept in their own card rather than folded into
 * `CurrentSubscriptionCard`'s button row: these hit a different, console-only controller
 * (`/api/subscription-simulation/...`) that forces payment outcomes and background work
 * directly, rather than the integrator-facing API the rest of this screen exercises.
 */
export const SimulationHarnessCard = ({
  onSimulatePaymentOutcome,
  onAdvanceRenewal,
  onCloseUsagePeriod,
  onRunDueJobs,
  onOpenDataConsole,
  lastResult,
}: {
  onSimulatePaymentOutcome: () => void;
  onAdvanceRenewal: () => void;
  onCloseUsagePeriod: () => void;
  onRunDueJobs: () => void;
  onOpenDataConsole: () => void;
  lastResult: HarnessResult | null;
}) => {
  const { t } = useT("utilities.subscription-simulation");
  return (
  <Card className="rounded-xl p-4">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <div className="flex items-center gap-2">
          <Beaker className="h-4 w-4 text-muted-foreground" />
          <h3 className="font-semibold">{t("SIMULATION_HARNESS_CARD.SIMULATION_HARNESS")}</h3>
        </div>
        <p className="text-xs text-muted-foreground">
          {t("SIMULATION_HARNESS_CARD.FORCES_PAYMENT_OUTCOMES_AND")}
        </p>
      </div>

      <div className="flex flex-wrap gap-2">
        <Button size="sm" variant="outline" onClick={onSimulatePaymentOutcome}>
          {t("SIMULATION_HARNESS_CARD.SIMULATE_PAYMENT_OUTCOME")}
        </Button>
        <Button size="sm" variant="outline" onClick={onAdvanceRenewal}>
          {t("SIMULATION_HARNESS_CARD.ADVANCE_RENEWAL")}
        </Button>
        <Button size="sm" variant="outline" onClick={onCloseUsagePeriod}>
          {t("SIMULATION_HARNESS_CARD.CLOSE_USAGE_PERIOD")}
        </Button>
        <Button size="sm" variant="outline" onClick={onRunDueJobs}>
          {t("SIMULATION_HARNESS_CARD.RUN_DUE_JOBS")}
        </Button>
        <Button size="sm" variant="ghost" onClick={onOpenDataConsole}>
          {t("SIMULATION_HARNESS_CARD.DATA_CONSOLE")}
        </Button>
      </div>
    </div>

    {lastResult && (
      <div className="mt-4 space-y-3 border-t pt-3">
        {isJobRun(lastResult) ? (
          <>
            <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <span>{t("SIMULATION_HARNESS_CARD.RUN", { simulationRunId: lastResult.simulationRunId })}</span>
              <Badge variant="info" className="font-normal">
                {t("SIMULATION_HARNESS_CARD.CLAIMED", { claimed: lastResult.claimed })}</Badge>
              <Badge variant="success" className="font-normal">
                {t("SIMULATION_HARNESS_CARD.COMPLETED", { completed: lastResult.completed })}</Badge>
              <Badge variant="secondary" className="font-normal">
                {t("SIMULATION_HARNESS_CARD.NOT_DUE", { notDue: lastResult.notDue })}</Badge>
            </div>
            {lastResult.jobs.length > 0 && (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("SIMULATION_HARNESS_CARD.WORK_TYPE")}</TableHead>
                    <TableHead>{t("common:STATUS")}</TableHead>
                    <TableHead>{t("SIMULATION_HARNESS_CARD.DETAIL")}</TableHead>
                    <TableHead>{t("SIMULATION_HARNESS_CARD.DURATION")}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {lastResult.jobs.map((job) => (
                    <TableRow key={job.workType}>
                      <TableCell className="text-xs">{job.workType}</TableCell>
                      <TableCell className="text-xs">{job.status}</TableCell>
                      <TableCell className="text-xs text-muted-foreground">
                        {job.detail ?? "—"}
                      </TableCell>
                      {/* eslint-disable-next-line no-restricted-syntax -- unit symbol */}
                      <TableCell className="text-xs">{job.durationMs} ms</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </>
        ) : (
          <>
            <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <span>{lastResult.action}</span>
              <span>{t("SIMULATION_HARNESS_CARD.RUN", { simulationRunId: lastResult.simulationRunId })}</span>
              <span>{formatDate(lastResult.completedAtUtc)}</span>
            </div>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead />
                  <TableHead>{t("SIMULATION_HARNESS_CARD.BEFORE")}</TableHead>
                  <TableHead>{t("SIMULATION_HARNESS_CARD.AFTER")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow>
                  <TableCell className="text-xs font-medium">{t("common:STATUS")}</TableCell>
                  <TableCell className="text-xs">{lastResult.before.subscriptionStatus}</TableCell>
                  <TableCell className="text-xs">{lastResult.after.subscriptionStatus}</TableCell>
                </TableRow>
                <TableRow>
                  <TableCell className="text-xs font-medium">{t("SIMULATION_HARNESS_CARD.CURRENT_PERIOD_ENDS")}</TableCell>
                  <TableCell className="text-xs">
                    {formatDate(lastResult.before.currentPeriodEndUtc)}
                  </TableCell>
                  <TableCell className="text-xs">
                    {formatDate(lastResult.after.currentPeriodEndUtc)}
                  </TableCell>
                </TableRow>
                <TableRow>
                  <TableCell className="text-xs font-medium">{t("SIMULATION_HARNESS_CARD.NEXT_FEE_BILLING")}</TableCell>
                  <TableCell className="text-xs">
                    {formatDate(lastResult.before.nextFeeBillingAtUtc)}
                  </TableCell>
                  <TableCell className="text-xs">
                    {formatDate(lastResult.after.nextFeeBillingAtUtc)}
                  </TableCell>
                </TableRow>
                <TableRow>
                  <TableCell className="text-xs font-medium">{t("SIMULATION_HARNESS_CARD.DUNNING_ATTEMPTS")}</TableCell>
                  <TableCell className="text-xs">{lastResult.before.dunningAttemptCount}</TableCell>
                  <TableCell className="text-xs">{lastResult.after.dunningAttemptCount}</TableCell>
                </TableRow>
                <TableRow>
                  <TableCell className="text-xs font-medium">{t("SIMULATION_HARNESS_CARD.VERSION")}</TableCell>
                  <TableCell className="text-xs">{lastResult.before.version}</TableCell>
                  <TableCell className="text-xs">{lastResult.after.version}</TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </>
        )}
      </div>
    )}
  </Card>
);
};
