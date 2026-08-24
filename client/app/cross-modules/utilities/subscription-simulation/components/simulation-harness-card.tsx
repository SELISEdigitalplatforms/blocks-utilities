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
}) => (
  <Card className="rounded-xl p-4">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <div className="flex items-center gap-2">
          <Beaker className="h-4 w-4 text-muted-foreground" />
          <h3 className="font-semibold">Simulation harness</h3>
        </div>
        <p className="text-xs text-muted-foreground">
          Forces payment outcomes and background work directly, for scenarios the plan catalogue
          and actions above cannot reach on their own — console-only, and unavailable unless the
          server has the harness enabled.
        </p>
      </div>

      <div className="flex flex-wrap gap-2">
        <Button size="sm" variant="outline" onClick={onSimulatePaymentOutcome}>
          Simulate payment outcome
        </Button>
        <Button size="sm" variant="outline" onClick={onAdvanceRenewal}>
          Advance renewal
        </Button>
        <Button size="sm" variant="outline" onClick={onCloseUsagePeriod}>
          Close usage period
        </Button>
        <Button size="sm" variant="outline" onClick={onRunDueJobs}>
          Run due jobs
        </Button>
        <Button size="sm" variant="ghost" onClick={onOpenDataConsole}>
          Data console
        </Button>
      </div>
    </div>

    {lastResult && (
      <div className="mt-4 space-y-3 border-t pt-3">
        {isJobRun(lastResult) ? (
          <>
            <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <span>Run {lastResult.simulationRunId}</span>
              <Badge variant="info" className="font-normal">
                {lastResult.claimed} claimed
              </Badge>
              <Badge variant="success" className="font-normal">
                {lastResult.completed} completed
              </Badge>
              <Badge variant="secondary" className="font-normal">
                {lastResult.notDue} not due
              </Badge>
            </div>
            {lastResult.jobs.length > 0 && (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Work type</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead>Detail</TableHead>
                    <TableHead>Duration</TableHead>
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
              <span>Run {lastResult.simulationRunId}</span>
              <span>{formatDate(lastResult.completedAtUtc)}</span>
            </div>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead />
                  <TableHead>Before</TableHead>
                  <TableHead>After</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow>
                  <TableCell className="text-xs font-medium">Status</TableCell>
                  <TableCell className="text-xs">{lastResult.before.subscriptionStatus}</TableCell>
                  <TableCell className="text-xs">{lastResult.after.subscriptionStatus}</TableCell>
                </TableRow>
                <TableRow>
                  <TableCell className="text-xs font-medium">Current period ends</TableCell>
                  <TableCell className="text-xs">
                    {formatDate(lastResult.before.currentPeriodEndUtc)}
                  </TableCell>
                  <TableCell className="text-xs">
                    {formatDate(lastResult.after.currentPeriodEndUtc)}
                  </TableCell>
                </TableRow>
                <TableRow>
                  <TableCell className="text-xs font-medium">Next fee billing</TableCell>
                  <TableCell className="text-xs">
                    {formatDate(lastResult.before.nextFeeBillingAtUtc)}
                  </TableCell>
                  <TableCell className="text-xs">
                    {formatDate(lastResult.after.nextFeeBillingAtUtc)}
                  </TableCell>
                </TableRow>
                <TableRow>
                  <TableCell className="text-xs font-medium">Dunning attempts</TableCell>
                  <TableCell className="text-xs">{lastResult.before.dunningAttemptCount}</TableCell>
                  <TableCell className="text-xs">{lastResult.after.dunningAttemptCount}</TableCell>
                </TableRow>
                <TableRow>
                  <TableCell className="text-xs font-medium">Version</TableCell>
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
