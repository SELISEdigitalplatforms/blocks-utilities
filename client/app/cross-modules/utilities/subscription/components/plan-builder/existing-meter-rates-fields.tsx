import { Plus, X } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { SUBSCRIPTION_CURRENCY_OPTIONS } from "../../constants/subscription.constants";
import type { MeterRateTable, PlanMeter } from "../../models/subscription-plan.model";
import {
  exampleMinorAmount,
  isRepresentableInMinorUnits,
  minorUnitStep,
  toMajorUnits,
  toMinorUnits,
} from "../../utilities/subscription-format";

interface DraftTier {
  /** Blank on the final, unbounded band — never edited, only ever cleared or derived. */
  upToQuantity: string;
  unitAmount: string;
}

interface DraftTable {
  currencyCode: string;
  tiers: DraftTier[];
}

const draftTablesFrom = (rateTables: MeterRateTable[] | undefined): DraftTable[] =>
  (rateTables ?? []).map((table) => ({
    currencyCode: table.currencyCode,
    tiers: table.tiers.map((tier) => ({
      upToQuantity: tier.upToQuantity === null ? "" : String(tier.upToQuantity),
      unitAmount: String(toMajorUnits(tier.unitAmountMinor, table.currencyCode)),
    })),
  }));

/**
 * The same ordering rule the server enforces — ascending, only the last band unbounded — checked
 * here too so a table that will be refused says why before the request is even sent.
 */
const validateDraftTables = (tables: DraftTable[]): string | null => {
  for (const table of tables) {
    if (table.tiers.length === 0) {
      return `${table.currencyCode} has no bands. Add at least one, or remove the currency.`;
    }

    let previousBound = 0;

    for (const [index, tier] of table.tiers.entries()) {
      const isLast = index === table.tiers.length - 1;
      const amount = Number(tier.unitAmount);

      if (!Number.isFinite(amount) || amount < 0) {
        return `Every band in ${table.currencyCode} needs a per-unit amount of zero or more.`;
      }

      if (!isRepresentableInMinorUnits(amount, table.currencyCode)) {
        return `${tier.unitAmount} ${table.currencyCode} has more decimal places than the currency allows.`;
      }

      if (isLast) {
        continue;
      }

      const bound = Number(tier.upToQuantity);

      if (!Number.isFinite(bound) || bound <= previousBound) {
        return `${table.currencyCode}'s bands must ascend — each "up to" must be greater than the one before it.`;
      }

      previousBound = bound;
    }
  }

  return null;
};

const toRateTableRequests = (tables: DraftTable[]): MeterRateTable[] =>
  tables.map((table) => ({
    currencyCode: table.currencyCode,
    tiers: table.tiers.map((tier, index) => ({
      upToQuantity:
        index === table.tiers.length - 1 ? null : Number(tier.upToQuantity),
      unitAmountMinor: toMinorUnits(Number(tier.unitAmount), table.currencyCode),
    })),
  }));

/**
 * One meter's overage rate tables, editable whether or not the plan already has subscribers.
 *
 * Its own endpoint and its own editor, the same deliberate exception price tax and discount get:
 * a subscription rates overage from the meter snapshot copied onto it at signup and never reads
 * the catalogue again, so a change here reaches only future subscriptions and future renewals —
 * nobody already on the plan is repriced by it.
 */
const ExistingMeterRatesEditor = ({
  meter,
  onSave,
}: {
  meter: PlanMeter;
  onSave: (meterKey: string, rateTables: MeterRateTable[]) => Promise<void>;
}) => {
  const [tables, setTables] = useState<DraftTable[]>(() => draftTablesFrom(meter.rateTables));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const updateTable = (tableIndex: number, next: DraftTable) =>
    setTables((current) => current.map((table, index) => (index === tableIndex ? next : table)));

  return (
    <div className="mt-2 space-y-3 rounded-md border border-dashed p-2">
      {tables.map((table, tableIndex) => (
        <div key={tableIndex} className="space-y-2">
          <div className="flex items-end gap-2">
            <Select
              value={table.currencyCode}
              onValueChange={(value) =>
                updateTable(tableIndex, { ...table, currencyCode: value })
              }
            >
              <SelectTrigger
                aria-label={`Overage currency for ${meter.displayName}`}
                className="w-28"
              >
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {SUBSCRIPTION_CURRENCY_OPTIONS.map((currency) => (
                  <SelectItem key={currency.code} value={currency.code}>
                    {currency.code}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label="Remove currency"
              className="text-muted-foreground hover:text-destructive"
              onClick={() =>
                setTables((current) => current.filter((_, index) => index !== tableIndex))
              }
            >
              <X className="h-4 w-4" />
            </Button>
          </div>

          {table.tiers.map((tier, tierIndex) => {
            const isLast = tierIndex === table.tiers.length - 1;

            return (
              <div key={tierIndex} className="flex items-end gap-2">
                <Input
                  aria-label={`Up to, band ${tierIndex + 1}`}
                  type="number"
                  min={1}
                  disabled={isLast}
                  value={isLast ? "" : tier.upToQuantity}
                  placeholder={isLast ? "no limit" : "1000"}
                  onChange={(event) =>
                    updateTable(tableIndex, {
                      ...table,
                      tiers: table.tiers.map((candidate, index) =>
                        index === tierIndex
                          ? { ...candidate, upToQuantity: event.target.value }
                          : candidate,
                      ),
                    })
                  }
                />
                <Input
                  aria-label={`Per unit (${table.currencyCode}), band ${tierIndex + 1}`}
                  type="number"
                  min={0}
                  step={minorUnitStep(table.currencyCode)}
                  placeholder={exampleMinorAmount(table.currencyCode)}
                  value={tier.unitAmount}
                  onChange={(event) =>
                    updateTable(tableIndex, {
                      ...table,
                      tiers: table.tiers.map((candidate, index) =>
                        index === tierIndex
                          ? { ...candidate, unitAmount: event.target.value }
                          : candidate,
                      ),
                    })
                  }
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  disabled={table.tiers.length === 1}
                  aria-label="Remove band"
                  className="text-muted-foreground hover:text-destructive"
                  onClick={() =>
                    updateTable(tableIndex, {
                      ...table,
                      tiers: table.tiers.filter((_, index) => index !== tierIndex),
                    })
                  }
                >
                  <X className="h-4 w-4" />
                </Button>
              </div>
            );
          })}

          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => {
              const lastIndex = table.tiers.length - 1;
              const last = table.tiers[lastIndex];
              const previousBound = Number(table.tiers[lastIndex - 1]?.upToQuantity) || 0;

              updateTable(tableIndex, {
                ...table,
                tiers: [
                  ...table.tiers.slice(0, lastIndex),
                  { upToQuantity: String(previousBound ? previousBound * 2 : 1_000), unitAmount: last?.unitAmount ?? "0" },
                  { upToQuantity: "", unitAmount: "0" },
                ],
              });
            }}
          >
            <Plus className="mr-2 h-4 w-4" />
            Add another band
          </Button>
        </div>
      ))}

      <Button
        type="button"
        variant="outline"
        size="sm"
        className="w-full"
        onClick={() =>
          setTables((current) => [
            ...current,
            { currencyCode: SUBSCRIPTION_CURRENCY_OPTIONS[0].code, tiers: [{ upToQuantity: "", unitAmount: "0" }] },
          ])
        }
      >
        <Plus className="mr-2 h-4 w-4" />
        {tables.length === 0 ? "Price the overage" : "Add another currency"}
      </Button>

      {tables.length > 0 && (
        <Button
          type="button"
          size="sm"
          disabled={saving}
          onClick={async () => {
            const validationError = validateDraftTables(tables);

            if (validationError) {
              setError(validationError);
              return;
            }

            setSaving(true);
            setError(null);

            try {
              await onSave(meter.meterKey, toRateTableRequests(tables));
            } catch (reason) {
              setError(reason instanceof Error ? reason.message : "The rates could not be saved.");
            } finally {
              setSaving(false);
            }
          }}
        >
          {saving ? "Saving…" : "Save overage rates"}
        </Button>
      )}

      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
};

/**
 * The plan's meters that allow overage, each with its own rate-table editor.
 *
 * Shown wherever a plan's own terms are otherwise closed — once anybody has subscribed, the
 * builder that authors meters is no longer reachable, and without this a plan's overage pricing
 * would be frozen forever the moment it was first sold.
 */
export const ExistingMeterRatesFields = ({
  meters,
  onSave,
}: {
  /** The plan's meters, as loaded — only those with overage allowed render an editor. */
  meters: PlanMeter[];
  onSave: (meterKey: string, rateTables: MeterRateTable[]) => Promise<void>;
}) => {
  const overageMeters = meters.filter((meter) => meter.overageAllowed);

  if (overageMeters.length === 0) {
    return null;
  }

  return (
    <div className="space-y-3">
      <div>
        <h3 className="text-sm font-semibold">Overage rates</h3>
        <p className="mt-1 text-xs text-muted-foreground">
          What usage past each meter&apos;s allowance costs. Nobody already subscribed is
          repriced: overage is rated from what was on the plan when they signed up, so this
          reaches only future subscriptions and future renewals.
        </p>
      </div>

      <div className="rounded-lg border border-border/70 bg-muted/40 p-3">
        <ul className="space-y-3">
          {overageMeters.map((meter) => (
            <li key={meter.meterKey} className="text-sm">
              <span className="font-medium">{meter.displayName}</span>
              <span className="ml-2 text-xs text-muted-foreground">per {meter.unitLabel}</span>
              <ExistingMeterRatesEditor meter={meter} onSave={onSave} />
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
};
