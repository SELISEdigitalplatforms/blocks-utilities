import { useFieldArray, useFormContext, useWatch } from "react-hook-form";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
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
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { CardListItem, CardListShell } from "./card-list-shell";

export const StepTrial = () => {
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const trialGrants = useFieldArray({ control, name: "trialGrants" });
  const trialDays = useWatch({ control, name: "trialDays" });
  const trialRequiresPaymentMethod = useWatch({
    control,
    name: "trialRequiresPaymentMethod",
  });
  const meters = useWatch({ control, name: "meters" });

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-lg font-semibold">Trial</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Optional. Leave the trial length blank for a plan that charges from day one.
        </p>
      </div>

      <div className="grid gap-5 sm:grid-cols-2">
        <FormField
          control={control}
          name="trialDays"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Trial length (days)</FormLabel>
              <FormControl>
                <Input {...field} value={field.value ?? ""} type="number" min={1} max={365} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={control}
          name="trialRequiresPaymentMethod"
          render={({ field }) => (
            <FormItem className="flex flex-col justify-end">
              <div className="flex items-center gap-2">
                <FormControl>
                  <Checkbox checked={field.value} onCheckedChange={field.onChange} />
                </FormControl>
                <FormLabel className="!m-0">Require a card to start the trial</FormLabel>
              </div>
              <p className="text-xs text-muted-foreground">
                {trialRequiresPaymentMethod
                  ? "The first period is charged at signup — the payment path cannot hold a card without charging it. The trial then only governs the allowances below."
                  : "Genuinely free until the trial ends, when the first charge is taken. Your product must collect a card before then, or that charge has nothing to bill."}
              </p>
            </FormItem>
          )}
        />
      </div>

      {Boolean(trialDays) && (
        <div className="space-y-3">
          <h3 className="text-sm font-semibold">Trial grants</h3>
          <p className="text-xs text-muted-foreground">
            Give a trial its own allowance for a meter instead of the plan&apos;s normal one — useful
            when the regular allowance would be an open invitation to sign up, consume and leave.
          </p>
          <CardListShell
            addLabel="Add trial grant"
            onAdd={() => trialGrants.append({ meterKey: "", includedQuantity: 0 })}
          >
            {trialGrants.fields.map((field, index) => (
              <CardListItem key={field.id} onRemove={() => trialGrants.remove(index)}>
                <FormField
                  control={control}
                  name={`trialGrants.${index}.meterKey`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">Meter</FormLabel>
                      <Select value={inputField.value} onValueChange={inputField.onChange}>
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
                  name={`trialGrants.${index}.includedQuantity`}
                  render={({ field: inputField }) => (
                    <FormItem>
                      <FormLabel className="text-xs">Included during trial</FormLabel>
                      <FormControl>
                        <Input {...inputField} type="number" min={0} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              </CardListItem>
            ))}
          </CardListShell>
        </div>
      )}
    </div>
  );
};
