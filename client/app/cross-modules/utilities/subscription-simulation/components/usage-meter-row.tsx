import { CheckCircle2, Loader2, ShieldAlert } from "lucide-react";
import { useState } from "react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { toast } from "@/hooks/use-toast";
import { useRecordUsage } from "../hooks/use-record-usage";
import type { MeterUsage } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";
import type {
  PlanMeter,
  PlanMeterSubLimit,
} from "../../subscription/models/subscription-plan.model";
import { describePaceWindow } from "../../subscription/utilities/member-plan-format";
import { useT } from "@seliseblocks/genesis-os";

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
const describeLimit = (limit: PlanMeterSubLimit) =>
  `${limit.quantity.toLocaleString()} ${describePaceWindow(limit.window, limit.windowCount, limit.rolling)}`;

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
  const { t } = useT("utilities.subscription-simulation");
  const { mutateAsync: recordUsage } = useRecordUsage();

  const [quantity, setQuantity] = useState("1");
  const [phase, setPhase] = useState<"idle" | "checking" | "recording">("idle");
  // A record answers with the balance including that call, so it is newer than the read it was
  // made against — and only that read. Kept with it, and shown while that read is still the one
  // on screen.
  const [recorded, setRecorded] = useState<{ result: MeterUsage; against: MeterUsage | undefined } | null>(
    null,
  );
  // Three outcomes of a recording, not two. "Over pace" is allowed-but-past-the-short-window-cap:
  // neither fine nor blocked, and folded into either one the whole pace behaviour is invisible —
  // a tester would conclude the cap does nothing.
  const [lastResult, setLastResult] = useState<
    { message: string; tone: "success" | "over-pace" | "blocked" | "error" } | null
  >(null);
  const pace = meter.subLimits?.length ? meter.subLimits.map(describeLimit).join(", ") : null;

  // Any fresh read takes back over. Comparing totals instead could not tell a read that had not
  // caught up from one for another place: moving from a place 130 in to an empty one kept showing
  // 130, because the new place's 0 looked like a read that was behind.
  const current = recorded && recorded.against === usage ? recorded.result : usage;

  const consume = async () => {
    const parsedQuantity = Number(quantity);
    if (!Number.isFinite(parsedQuantity) || parsedQuantity <= 0) {
      setLastResult({ message: t("USAGE_METER_ROW.ENTER_A_QUANTITY_GREATER_THAN"), tone: "error" });
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
            message: t("USAGE_METER_ROW.BLOCKED_BEFORE_RECORDING", { reason: checked.reason }),
            tone: "blocked",
          });
          return;
        }

        // A meter that bills overage has no stopping point: `remaining` is only what is left
        // before overage starts.
        if (
          checked.limitKind === "Count" &&
          !checked.overageAllowed &&
          checked.remaining != null &&
          checked.remaining < parsedQuantity
        ) {
          setLastResult({
            message: t("USAGE_METER_ROW.BLOCKED_BEFORE_RECORDING_ONLY", { remaining: checked.remaining, unit: `${meter.unitLabel}${checked.remaining === 1 ? "" : "s"}` }),
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

      setRecorded({ result, against: usage });

      // The server names every limit this use went past, so the message can say which one — a
      // meter with several limits would otherwise say "over pace" and leave the tester guessing.
      // Split by what each limit does. A refusal names only the limits that refused — naming a
      // reporting one beside it read as though both had stopped the use — and says separately
      // which it was merely over.
      const passed = result.exceededSubLimits ?? [];
      const exceeded = passed.map(describeLimit).join(" and ");
      const refusing = passed.filter((limit) => limit.behaviour === "Refuse").map(describeLimit).join(" and ");
      const reporting = passed.filter((limit) => limit.behaviour !== "Refuse").map(describeLimit).join(" and ");
      const refusedByPace = !result.allowed && refusing !== "";
      const units = `${result.unitLabel}${result.included === 1 ? "" : "s"}`;
      const recordedLine = result.overage
        ? t("USAGE_METER_ROW.RECORDED_WITH_OVERAGE", { used: result.used, included: result.included, units, remaining: result.remaining, overage: result.overage })
        : t("USAGE_METER_ROW.RECORDED", { used: result.used, included: result.included, units, remaining: result.remaining });

      setLastResult(
        result.allowed && result.subLimitExceeded
          ? {
              message: t("USAGE_METER_ROW.PAST_THE_PACE_OF_ALLOWED", { recordedLine, pace: exceeded || pace || t("USAGE_METER_ROW.THIS_METER") }),
              tone: "over-pace",
            }
          : result.allowed
            ? { message: recordedLine, tone: "success" }
            : {
                message: refusedByPace
                  ? t("USAGE_METER_ROW.REFUSED_BY_THE_PACE_LIMIT", { refusing, also: reporting ? t("USAGE_METER_ROW.ALSO_OVER_THE_PACE", { reporting }) : "" })
                  : t("USAGE_METER_ROW.REFUSED_BY_THE_USAGE_CALL"),
                tone: "blocked",
              },
      );

      if (!result.allowed) {
        toast({
          variant: "destructive",
          title: t("USAGE_METER_ROW.USAGE_REFUSED"),
          description: refusedByPace
            ? t("USAGE_METER_ROW.IS_OVER_ITS_PACE_OF", { displayName: meter.displayName, refusing })
            : t("USAGE_METER_ROW.HAS_NO_REMAINING_ALLOWANCE", { displayName: meter.displayName }),
        });
      }
    } catch (error) {
      setLastResult({
        message: error instanceof Error ? error.message : t("USAGE_METER_ROW.THE_USAGE_CALL_FAILED"),
        tone: "error",
      });
    } finally {
      setPhase("idle");
    }
  };

  return (
    <div className="flex flex-col gap-2 border-b py-3 last:border-b-0 sm:flex-row sm:items-center sm:justify-between">
      <div className="min-w-0">
        <p className="flex items-center gap-2 text-sm font-medium">
          {meter.displayName}
          {lastResult?.tone === "over-pace" ? (
            <Badge variant="outline" className="border-amber-300 bg-amber-50 font-normal text-amber-800">
              {t("USAGE_METER_ROW.OVER_PACE")}
            </Badge>
          ) : null}
          {pace ? <span className="text-xs font-normal text-muted-foreground">{t("USAGE_METER_ROW.PACE_VALUE", { pace })}</span> : null}
        </p>
        <p className="text-xs text-muted-foreground">
          {current
            ? current.overage
              ? t("USAGE_METER_ROW.USED_THIS_PERIOD_OVER", { used: current.used, included: current.included, units: `${current.unitLabel || meter.unitLabel}${current.included === 1 ? "" : "s"}`, overage: current.overage })
              : t("USAGE_METER_ROW.USED_THIS_PERIOD", { used: current.used, included: current.included, units: `${current.unitLabel || meter.unitLabel}${current.included === 1 ? "" : "s"}` })
            : entitlementKey
              ? t("USAGE_METER_ROW.ENTITLEMENT", { entitlementKey })
              : t("USAGE_METER_ROW.NO_ENTITLEMENT_GATES_THIS_METER")}
        </p>
        {lastResult && (
          <p
            className={
              "mt-1 flex items-center gap-1 text-xs " +
              (lastResult.tone === "success"
                ? "text-green-700"
                : lastResult.tone === "over-pace"
                  ? "text-amber-700"
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
          aria-label={t("USAGE_METER_ROW.QUANTITY_TO_CONSUME_FOR", { displayName: meter.displayName })}
        />
        <Button size="sm" onClick={consume} disabled={phase !== "idle"}>
          {phase !== "idle" && <Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" />}
          {phase === "checking" ? t("USAGE_METER_ROW.CHECKING") : phase === "recording" ? t("USAGE_METER_ROW.RECORDING") : t("USAGE_METER_ROW.CONSUME")}
        </Button>
      </div>
    </div>
  );
};
