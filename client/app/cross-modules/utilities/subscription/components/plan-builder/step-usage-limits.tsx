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
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { CardListItem, CardListShell } from "./card-list-shell";

export const StepUsageLimits = () => {
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const entitlements = useFieldArray({ control, name: "entitlements" });
  const meters = useWatch({ control, name: "meters" });

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-lg font-semibold">Usage limits</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          What a subscriber on this plan is allowed to do — separate from what they're billed for.
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
          const limitKind = field.limitKind;

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
                      onValueChange={(value) => inputField.onChange(Number(value))}
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
            </CardListItem>
          );
        })}
      </CardListShell>
    </div>
  );
};
