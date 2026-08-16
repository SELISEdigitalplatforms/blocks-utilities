import { AlertTriangle } from "lucide-react";
import { useFieldArray, useFormContext, useWatch } from "react-hook-form";
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
import { ENTITLEMENT_LIMIT_KIND_OPTIONS } from "../../constants/subscription.constants";
import { ENTITLEMENT_LIMIT_KIND_NAMES } from "../../models/subscription-plan.model";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { describeEntitlementMeterMismatch } from "../../utilities/plan-consistency";
import { CardListItem, CardListShell } from "./card-list-shell";

export const StepUsageLimits = () => {
  const { control, setValue } = useFormContext<CreateSubscriptionPlanFormValues>();
  const entitlements = useFieldArray({ control, name: "entitlements" });
  const meters = useWatch({ control, name: "meters" });
  // Watched rather than read off useFieldArray's `fields`: that array is a snapshot taken when
  // the list itself changes, so editing a card's own Kind would never reveal the fields that
  // depend on it.
  const entitlementValues = useWatch({ control, name: "entitlements" });

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-lg font-semibold">Usage limits</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          What a subscriber on this plan is allowed to do — separate from what they&apos;re billed for.
          Skip this if the plan just grants everything.
        </p>
      </div>

      <CardListShell
        addLabel="Add usage limit"
        onAdd={() =>
          entitlements.append({
            key: "",
            limitKind: 0,
          })
        }
      >
        {entitlements.fields.map((field, index) => {
          const limitKind = entitlementValues?.[index]?.limitKind ?? field.limitKind;
          const draft = entitlementValues?.[index];
          const mismatch = draft
            ? describeEntitlementMeterMismatch(
                {
                  limitKind: ENTITLEMENT_LIMIT_KIND_NAMES[draft.limitKind ?? 0],
                  limit: draft.limit ?? null,
                  meterKey: draft.meterKey ?? null,
                },
                (meters ?? []).map((meter) => ({
                  meterKey: meter.meterKey ?? "",
                  unitLabel: meter.unitLabel ?? "",
                  includedQuantity: meter.includedQuantity ?? 0,
                  overageAllowed: meter.overageAllowed ?? false,
                })),
              )
            : null;

          return (
            <CardListItem key={field.id} onRemove={() => entitlements.remove(index)}>
              <FormField
                control={control}
                name={`entitlements.${index}.key`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">Key</FormLabel>
                    <FormControl>
                      <Input {...inputField} placeholder="advanced-reports" />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={control}
                name={`entitlements.${index}.limitKind`}
                render={({ field: inputField }) => (
                  <FormItem>
                    <FormLabel className="text-xs">Kind</FormLabel>
                    <Select
                      value={String(inputField.value)}
                      onValueChange={(value) => {
                        const kind = Number(value);
                        inputField.onChange(kind);

                        // Only a counted entitlement carries these. Leaving them behind on a
                        // switch would submit a meter reference the plan may no longer define,
                        // which the server rejects outright.
                        if (kind !== 1) {
                          setValue(`entitlements.${index}.meterKey`, undefined);
                          setValue(`entitlements.${index}.limit`, undefined);
                        }
                      }}
                    >
                      <FormControl>
                        <SelectTrigger>
                          <SelectValue />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        {ENTITLEMENT_LIMIT_KIND_OPTIONS.map((option) => (
                          <SelectItem key={option.value} value={String(option.value)}>
                            {option.label}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <FormMessage />
                  </FormItem>
                )}
              />

              {limitKind === 1 && (
                <>
                  <FormField
                    control={control}
                    name={`entitlements.${index}.meterKey`}
                    render={({ field: inputField }) => (
                      <FormItem>
                        <FormLabel className="text-xs">Draws down which meter</FormLabel>
                        <Select value={inputField.value ?? ""} onValueChange={inputField.onChange}>
                          <FormControl>
                            <SelectTrigger>
                              <SelectValue placeholder="Choose a meter" />
                            </SelectTrigger>
                          </FormControl>
                          <SelectContent>
                            {(meters ?? [])
                              .filter((meter) => meter.meterKey)
                              .map((meter) => (
                                <SelectItem key={meter.meterKey} value={meter.meterKey}>
                                  {meter.displayName || meter.meterKey}
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
                    name={`entitlements.${index}.limit`}
                    render={({ field: inputField }) => (
                      <FormItem>
                        <FormLabel className="text-xs">Limit</FormLabel>
                        <FormControl>
                          <Input {...inputField} type="number" min={0} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </>
              )}

              {/* A warning, not a validation error: permitting more than the meter includes is a
                  real configuration — the excess is simply billed as overage — so this says what
                  will happen rather than refusing to submit it. */}
              {mismatch && (
                <p className="flex items-start gap-1.5 text-xs text-warning-700">
                  <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                  {mismatch}
                </p>
              )}
            </CardListItem>
          );
        })}
      </CardListShell>
    </div>
  );
};
