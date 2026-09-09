import { CheckCircle2, Loader2, ShieldAlert } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { toast } from "@/hooks/use-toast";
import { useRecordUsage } from "../hooks/use-record-usage";
import type { MeterUsage } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";
import type { PlanMeter } from "../../subscription/models/subscription-plan.model";

/**
 * One meter's consume control.
 *
 * Deliberately two steps, matching how an integrator would naturally reach for the API: read the
 * entitlement right before acting, decide client-side, then record. The docs are explicit that
 * this check is advisory rather than a lock — two callers a unit under the limit can both read
 * `allowed: true` — so the record call still carries `enforce: true` as the real gate. Watching
 * the "checked" and "recorded" balances diverge under fast repeated clicks is the point of
 * simulating it this way rather than only calling the usage endpoint directly.
 */
export const UsageMeterRow = ({
  meter,
  entitlementKey,
  usage,
  organizationId,
}: {
  meter: PlanMeter;
  /** The plan entitlement that gates this meter, if the plan defines one. */
  entitlementKey: string | undefined;
  /** This meter's row from `GET /api/subscription-usage/current` — the authoritative figures. */
  usage: MeterUsage | undefined;
  organizationId: string | undefined;
}) => {
  const { mutateAsync: recordUsage } = useRecordUsage();

  const [quantity, setQuantity] = useState("1");
  const [phase, setPhase] = useState<"idle" | "checking" | "recording">("idle");
  // A record answers with the balance including that call, so it is newer than any read. It is
  // dropped as soon as a fresh read arrives for the same period.
  const [recorded, setRecorded] = useState<MeterUsage | null>(null);
  const [lastResult, setLastResult] = useState<
    { message: string; tone: "success" | "blocked" | "error" } | null
  >(null);

  // The record result wins while it is for the period the read describes; once the read catches
  // up (or the period turns over) the server's own row takes back over.
  const current =
    recorded && (!usage || (usage.periodKey === recorded.periodKey && usage.used < recorded.used))
      ? recorded
      : usage;

  const consume = async () => {
    const parsedQuantity = Number(quantity);
    if (!Number.isFinite(parsedQuantity) || parsedQuantity <= 0) {
      setLastResult({ message: "Enter a quantity greater than zero.", tone: "error" });
      return;
    }

    setLastResult(null);

    try {
      if (entitlementKey) {
        // Step 1 — check: read the entitlement fresh, right before acting.
        setPhase("checking");
        const checked = await subscriptionSimulationService.getEntitlement(
          entitlementKey,
          organizationId,
        );
        if (!checked.allowed) {
          setLastResult({
            message: `Blocked before recording — ${checked.reason}.`,
            tone: "blocked",
          });
          return;
        }

        if (
          checked.limitKind === "Count" &&
          checked.remaining != null &&
          checked.remaining < parsedQuantity
        ) {
          setLastResult({
            message: `Blocked before recording — only ${checked.remaining} ${meter.unitLabel}${checked.remaining === 1 ? "" : "s"} remaining.`,
            tone: "blocked",
          });
          return;
        }
      }

      // Step 2/3 — the check passed (or none applies), so record. `enforce: true` is the real
      // gate: the check above cannot see this exact quantity landing at the same instant another
      // one does.
      setPhase("recording");

      const result = await recordUsage({
        meterKey: meter.meterKey,
        quantity: parsedQuantity,
        idempotencyKey: crypto.randomUUID(),
        enforce: true,
        organizationId,
      });

      setRecorded(result);

      setLastResult({
        message: result.allowed
          ? `Recorded. ${result.used}/${result.included} ${result.unitLabel} used this period, ${result.remaining} remaining${result.overage ? `, ${result.overage} over` : ""}.`
          : "Refused by the usage call — the allowance was exhausted between the check and this call.",
        tone: result.allowed ? "success" : "blocked",
      });

      if (!result.allowed) {
        toast({
          variant: "destructive",
          title: "Usage refused",
          description: `${meter.displayName} has no remaining allowance.`,
        });
      }
    } catch (error) {
      setLastResult({
        message: error instanceof Error ? error.message : "The usage call failed.",
        tone: "error",
      });
    } finally {
      setPhase("idle");
    }
  };

  return (
    <div className="flex flex-col gap-2 border-b py-3 last:border-b-0 sm:flex-row sm:items-center sm:justify-between">
      <div className="min-w-0">
        <p className="text-sm font-medium">{meter.displayName}</p>
        <p className="text-xs text-muted-foreground">
          {current
            ? `${current.used}/${current.included} ${current.unitLabel || meter.unitLabel}${current.included === 1 ? "" : "s"} used this period${current.overage ? `, ${current.overage} over` : ""}`
            : entitlementKey
              ? `Entitlement: ${entitlementKey}`
              : `No entitlement gates this meter — recording goes straight through.`}
        </p>
        {lastResult && (
          <p
            className={
              "mt-1 flex items-center gap-1 text-xs " +
              (lastResult.tone === "success"
                ? "text-green-700"
                : lastResult.tone === "blocked"
                  ? "text-warning-800"
                  : "text-destructive")
            }
          >
            {lastResult.tone === "success" ? (
              <CheckCircle2 className="h-3.5 w-3.5" />
            ) : (
              <ShieldAlert className="h-3.5 w-3.5" />
            )}
            {lastResult.message}
          </p>
        )}
      </div>

      <div className="flex items-center gap-2">
        <Input
          type="number"
          min={1}
          value={quantity}
          onChange={(event) => setQuantity(event.target.value)}
          className="w-20"
          aria-label={`Quantity to consume for ${meter.displayName}`}
        />
        <Button size="sm" onClick={consume} disabled={phase !== "idle"}>
          {phase !== "idle" && <Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" />}
          {phase === "checking" ? "Checking…" : phase === "recording" ? "Recording…" : "Consume"}
        </Button>
      </div>
    </div>
  );
};
