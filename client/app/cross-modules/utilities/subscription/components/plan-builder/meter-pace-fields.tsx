import { ChevronDown, Plus, Trash2 } from "lucide-react";
import { useFieldArray, useFormContext, useWatch } from "react-hook-form";
import { Button } from "@/components/ui-kits/button/button";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { USAGE_WINDOW_NAMES } from "../../models/subscription-plan.model";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { stepFor } from "../../utilities/meter-quantity";
import { MAX_PACE_LIMITS, paceWarnings } from "../../utilities/pace-limits";

/**
 * Where a fixed window actually begins, in the terms somebody would read off a clock.
 *
 * Worth saying because the alternative is what most people assume: that the window runs from
 * whenever the subscriber signed up. It does not, deliberately — a pace measured from each
 * subscriber's own instant cannot be reasoned about by anybody comparing two of them.
 */
const PACE_WINDOW_BOUNDARIES = [
  "Each hour runs on the clock, 09:00 to 10:00 and so on — not from when the subscriber signed up.",
  "Each day runs midnight to midnight, UTC — not from when the subscriber signed up.",
  "Each week runs Monday to Monday — not from when the subscriber signed up.",
] as const;

const ROLLING_EXPLANATION =
  "A rolling window ends now and looks back — spend right up to the cap, wait, and the oldest " +
  "use ages out and makes room again.";

/** Reads as a ceiling, which is always of more than one. "token" alone reads as a typo. */
const pluralUnits = (unitLabel?: string) => {
  const label = unitLabel?.trim() || "units";

  return label.endsWith("s") ? label : `${label}s`;
};

const spanLabel = (window: number, count: number) =>
  `${count === 1 ? "" : `${count} `}${USAGE_WINDOW_NAMES[window]?.toLowerCase() ?? "window"}${count === 1 ? "" : "s"}`;

/**
 * Caps on how fast the allowance is spent, on top of the period's own amount — as many as three,
 * each counted on its own and each refusing or reporting on its own. Collapsed unless the meter
 * already has one, so every meter that never opted in looks exactly as it did.
 */
export const MeterPaceFields = ({
  meterIndex,
  unitLabel,
  quantityScale,
}: {
  meterIndex: number;
  unitLabel?: string;
  quantityScale: number;
}) => {
  const { control, getFieldState, trigger, formState } =
    useFormContext<CreateSubscriptionPlanFormValues>();
  const limits = useFieldArray({ control, name: `meters.${meterIndex}.subLimits` });
  const values = useWatch({ control, name: `meters.${meterIndex}.subLimits` }) ?? [];
  const meter = useWatch({ control, name: `meters.${meterIndex}` });
  const usageInterval = useWatch({ control, name: "usageInterval" });
  const usageIntervalCount = useWatch({ control, name: "usageIntervalCount" });

  const warnings = paceWarnings(
    values.map((limit) => ({
      window: limit?.window ?? 0,
      count: limit?.count ?? 1,
      rolling: limit?.rolling ?? false,
      quantity: limit?.quantity,
      behaviour: limit?.behaviour ?? 0,
    })),
    {
      includedQuantity: Number(meter?.includedQuantity ?? 0),
      usageInterval: Number(usageInterval ?? 2),
      usageIntervalCount: Number(usageIntervalCount ?? 1),
      resetPolicy: Number(meter?.resetPolicy ?? 0),
      overageAllowed: meter?.overageAllowed ?? false,
      carryForwardCap:
        meter?.carryForwardCap === undefined ? undefined : Number(meter.carryForwardCap),
    },
  );

  // Two of the rules — the same length twice, a longer limit allowing no more — are filed against
  // one row but caused by another, and the divide-a-day rule is filed on the count but fixed by
  // the rolling box. React Hook Form only revalidates the field that changed, so a fix made on one
  // row would leave another row's error standing. Re-checked only while something is already
  // wrong: this clears a stale error, it does not raise one before the author has left the field.
  const revalidate = () => {
    if (getFieldState(`meters.${meterIndex}.subLimits`, formState).invalid) {
      void trigger(`meters.${meterIndex}.subLimits`);
    }
  };

  const summary =
    values.length === 0
      ? ""
      : ` — ${values
          .map((limit) => `${limit?.rolling ? "any " : "per "}${spanLabel(limit?.window ?? 0, limit?.count ?? 1)}`)
          .join(", ")}`;

  return (
    <Collapsible defaultOpen={limits.fields.length > 0}>
      <CollapsibleTrigger className="flex items-center gap-1 text-xs font-medium text-muted-foreground hover:text-foreground">
        <ChevronDown className="h-3.5 w-3.5" />
        Pace limits (optional){summary}
      </CollapsibleTrigger>
      <CollapsibleContent className="space-y-3 pt-2">
        <p className="text-xs text-muted-foreground">
          Caps how fast the allowance is spent, not how much of it. An allowance with nothing
          shorter than the billing period can be spent in an afternoon. A use has to fit every
          limit here as well as the included amount.
        </p>

        {limits.fields.map((field, index) => {
          const limit = values[index];
          const window = limit?.window ?? 0;
          const count = limit?.count ?? 1;
          const span = spanLabel(window, count);

          return (
            <div key={field.id} className="space-y-2 rounded-md border p-3" data-testid="pace-limit">
              <div className="grid grid-cols-[1fr_1fr_1fr_auto] items-end gap-2">
                <FormField
                  control={control}
                  name={`meters.${meterIndex}.subLimits.${index}.window`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">Window</FormLabel>
                      <Select
                        value={String(inputField.value ?? 0)}
                        onValueChange={(value) => {
                          inputField.onChange(Number(value));
                          revalidate();
                        }}
                      >
                        <FormControl>
                          <SelectTrigger>
                            <SelectValue />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          {USAGE_WINDOW_NAMES.map((name, value) => (
                            <SelectItem key={name} value={String(value)}>
                              {name}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={control}
                  name={`meters.${meterIndex}.subLimits.${index}.count`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      {/* Always plural: it asks a question, and "How many week" reads as a typo. */}
                      <FormLabel className="text-xs">
                        How many {USAGE_WINDOW_NAMES[window]?.toLowerCase()}s
                      </FormLabel>
                      <FormControl>
                        <Input
                          {...inputField}
                          value={inputField.value ?? 1}
                          type="number"
                          min={1}
                          step={1}
                          onChange={(event) => {
                            inputField.onChange(Number(event.target.value) || 1);
                            revalidate();
                          }}
                        />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={control}
                  name={`meters.${meterIndex}.subLimits.${index}.quantity`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">
                        Most {pluralUnits(unitLabel)} per {span}
                      </FormLabel>
                      <FormControl>
                        <Input
                          {...inputField}
                          value={inputField.value ?? ""}
                          type="number"
                          min={stepFor(quantityScale)}
                          step={stepFor(quantityScale)}
                          onChange={(event) => {
                            inputField.onChange(event);
                            revalidate();
                          }}
                        />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  aria-label={`Remove the ${span} limit`}
                  onClick={() => {
                    limits.remove(index);
                    revalidate();
                  }}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>

              <div className="grid gap-2 sm:grid-cols-2">
                <FormField
                  control={control}
                  name={`meters.${meterIndex}.subLimits.${index}.rolling`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <div className="flex items-center gap-2">
                        <FormControl>
                          <Checkbox
                            checked={inputField.value}
                            onCheckedChange={(checked) => {
                              inputField.onChange(checked === true);
                              revalidate();
                            }}
                          />
                        </FormControl>
                        <FormLabel className="!m-0 text-xs">
                          Rolling — the last {span} from right now, not a window on the clock
                        </FormLabel>
                      </div>
                    </FormItem>
                  )}
                />
                <FormField
                  control={control}
                  name={`meters.${meterIndex}.subLimits.${index}.behaviour`}
                  render={({ field: inputField }) => (
                    <FormItem className="flex items-center gap-2 space-y-0">
                      <FormLabel className="shrink-0 text-xs">When exceeded</FormLabel>
                      <Select
                        value={String(inputField.value ?? 0)}
                        onValueChange={(value) => inputField.onChange(Number(value))}
                      >
                        <FormControl>
                          <SelectTrigger>
                            <SelectValue />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          {/* Short enough to read in full in a narrow row; the note under the
                              list says what reporting means. */}
                          <SelectItem value="0">Refuse</SelectItem>
                          <SelectItem value="1">Allow and report</SelectItem>
                        </SelectContent>
                      </Select>
                    </FormItem>
                  )}
                />
              </div>

              <p className="text-xs text-muted-foreground">
                {limit?.rolling ? ROLLING_EXPLANATION : PACE_WINDOW_BOUNDARIES[window]}
              </p>
              {warnings
                .filter((warning) => warning.index === index)
                .map((warning) => (
                  <p key={warning.message} role="note" className="text-xs text-amber-700">
                    {warning.message}
                  </p>
                ))}
            </div>
          );
        })}

        {/* The "at most three" rule is about the list, so its message sits under the list. */}
        <FormField
          control={control}
          name={`meters.${meterIndex}.subLimits`}
          render={() => <FormMessage />}
        />

        {warnings
          .filter((warning) => warning.index === -1)
          .map((warning) => (
            <p key={warning.message} role="note" className="text-xs text-amber-700">
              {warning.message}
            </p>
          ))}

        <div className="flex flex-wrap items-center gap-3">
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={limits.fields.length >= MAX_PACE_LIMITS}
            onClick={() =>
              limits.append({ window: 0, count: 1, rolling: false, quantity: undefined, behaviour: 0 })
            }
          >
            <Plus className="mr-1 h-4 w-4" />
            Add limit
          </Button>
          {limits.fields.length > 0 ? (
            <p className="text-xs text-muted-foreground">
              Reporting slows nobody down by itself — the caller is told it is over pace and
              decides what to do, e.g. fall back to a cheaper model.
            </p>
          ) : null}
        </div>
      </CollapsibleContent>
    </Collapsible>
  );
};
