import { useT } from "@seliseblocks/genesis-os";
import { useFieldArray, useFormContext, useWatch, type Control } from "react-hook-form";
import { Button } from "@/components/ui-kits/button/button";
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
import { SUBSCRIPTION_CURRENCY_OPTIONS } from "../../constants/subscription.constants";
import {
  CREDIT_EXPIRY_UNIT,
  FIXED_CREDIT_EXPIRY,
  METER_RESET_POLICY,
} from "../../models/subscription-plan.model";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import { TAX_MODE_OPTIONS } from "../../utilities/subscription-tax";
import { CardListItem, CardListShell } from "./card-list-shell";
import { StepHeading } from "./step-heading";

/** When bought credits stop being usable, worded for the person selling them. */
type Translate = ReturnType<typeof useT>["t"];

export const creditExpiryOptions = (
  t: Translate,
): { value: number; label: string; description: string }[] => [
  {
    value: METER_RESET_POLICY.Periodic,
    label: t("STEP_CREDITS.EXPIRY_PERIODIC"),
    description: t("STEP_CREDITS.EXPIRY_PERIODIC_DESCRIPTION"),
  },
  {
    value: METER_RESET_POLICY.CarryForward,
    label: t("STEP_CREDITS.EXPIRY_CARRY_FORWARD"),
    description: t("STEP_CREDITS.EXPIRY_CARRY_FORWARD_DESCRIPTION"),
  },
  {
    value: METER_RESET_POLICY.Never,
    label: t("STEP_CREDITS.EXPIRY_NEVER"),
    description: t("STEP_CREDITS.EXPIRY_NEVER_DESCRIPTION"),
  },
  {
    value: FIXED_CREDIT_EXPIRY,
    label: t("STEP_CREDITS.EXPIRY_FIXED"),
    description: t("STEP_CREDITS.EXPIRY_FIXED_DESCRIPTION"),
  },
];

export const creditExpiryUnitOptions = (t: Translate): { value: number; label: string }[] => [
  { value: CREDIT_EXPIRY_UNIT.Hour, label: t("STEP_CREDITS.UNIT_HOURS") },
  { value: CREDIT_EXPIRY_UNIT.Day, label: t("STEP_CREDITS.UNIT_DAYS") },
  { value: CREDIT_EXPIRY_UNIT.Week, label: t("STEP_CREDITS.UNIT_WEEKS") },
  { value: CREDIT_EXPIRY_UNIT.Month, label: t("STEP_CREDITS.UNIT_MONTHS") },
  { value: CREDIT_EXPIRY_UNIT.Year, label: t("STEP_CREDITS.UNIT_YEARS") },
];

export const StepCredits = () => {
  const { t } = useT("utilities.subscription.plan-builder");
  const { control } = useFormContext<CreateSubscriptionPlanFormValues>();
  const credits = useFieldArray({ control, name: "meterCredits" });
  const meters = useWatch({ control, name: "meters" }) ?? [];
  const values = useWatch({ control, name: "meterCredits" }) ?? [];

  const namedMeters = meters.filter((meter) => meter.meterKey);
  const unused = namedMeters.filter(
    (meter) => !values.some((credit) => credit?.meterKey === meter.meterKey),
  );

  return (
    <div className="space-y-6">
      <StepHeading
        eyebrow={t("STEP_CREDITS.EYEBROW")}
        title={t("STEP_CREDITS.TITLE")}
        description={t("STEP_CREDITS.DESCRIPTION")}
      />

      {namedMeters.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          {t("STEP_CREDITS.NO_METERS")}
        </p>
      ) : (
        <CardListShell
          addLabel={t("STEP_CREDITS.ADD_CREDITS")}
          columns={1}
          onAdd={() =>
            credits.append({
              // The first meter without terms. Empty once every meter has some, which the schema
              // then asks the author to choose.
              meterKey: unused[0]?.meterKey ?? "",
              enabled: true,
              prices: [{ currencyCode: "USD", unitAmount: 0.1 }],
              minQuantity: 1,
              maxQuantity: 10_000,
              expiryPolicy: METER_RESET_POLICY.Periodic,
              expiresAfterCount: undefined,
              expiresAfterUnit: CREDIT_EXPIRY_UNIT.Day,
              taxPercent: undefined,
              taxMode: "Exclusive",
            })
          }
        >
          {credits.fields.map((field, index) => (
            <CardListItem key={field.id} onRemove={() => credits.remove(index)}>
              <CreditFields control={control} index={index} meters={namedMeters} />
            </CardListItem>
          ))}
        </CardListShell>
      )}
    </div>
  );
};

const CreditFields = ({
  control,
  index,
  meters,
}: {
  control: Control<CreateSubscriptionPlanFormValues>;
  index: number;
  meters: { meterKey: string; displayName?: string }[];
}) => {
  const { t } = useT("utilities.subscription.plan-builder");
  const expiryOptions = creditExpiryOptions(t);
  const unitOptions = creditExpiryUnitOptions(t);
  const prices = useFieldArray({ control, name: `meterCredits.${index}.prices` });
  const enabled = useWatch({ control, name: `meterCredits.${index}.enabled` });
  const expiry = useWatch({ control, name: `meterCredits.${index}.expiryPolicy` });
  const taxPercent = useWatch({ control, name: `meterCredits.${index}.taxPercent` });

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField
          control={control}
          name={`meterCredits.${index}.meterKey`}
          render={({ field }) => (
            <FormItem>
              <FormLabel className="text-xs">{t("STEP_CREDITS.METER")}</FormLabel>
              <Select value={field.value} onValueChange={field.onChange}>
                <FormControl>
                  <SelectTrigger aria-label={t("STEP_CREDITS.METER")}>
                    <SelectValue placeholder={t("STEP_CREDITS.CHOOSE_METER")} />
                  </SelectTrigger>
                </FormControl>
                <SelectContent>
                  {meters.map((meter) => (
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
          name={`meterCredits.${index}.enabled`}
          render={({ field }) => (
            <FormItem className="flex flex-col justify-end">
              <div className="flex items-center gap-2">
                <FormControl>
                  <Checkbox checked={field.value} onCheckedChange={field.onChange} />
                </FormControl>
                <FormLabel className="!m-0">{t("STEP_CREDITS.ON_SALE")}</FormLabel>
              </div>
              <p className="text-xs text-muted-foreground">
                {enabled
                  ? t("STEP_CREDITS.ON_SALE_HINT")
                  : t("STEP_CREDITS.PAUSED_HINT")}
              </p>
            </FormItem>
          )}
        />
      </div>

      <div className="space-y-2">
        <h4 className="text-xs font-semibold">{t("STEP_CREDITS.PRICE_PER_CREDIT")}</h4>
        {prices.fields.map((priceField, priceIndex) => (
          <div key={priceField.id} className="grid grid-cols-[6rem_minmax(0,1fr)_auto] items-start gap-2">
            <FormField
              control={control}
              name={`meterCredits.${index}.prices.${priceIndex}.currencyCode`}
              render={({ field }) => (
                <FormItem>
                  <Select value={field.value} onValueChange={field.onChange}>
                    <FormControl>
                      <SelectTrigger aria-label={t("STEP_CREDITS.CURRENCY")}>
                        <SelectValue placeholder={t("STEP_CREDITS.CURRENCY")} />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      {SUBSCRIPTION_CURRENCY_OPTIONS.map((currency) => (
                        <SelectItem key={currency.code} value={currency.code}>
                          {currency.code}
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
              name={`meterCredits.${index}.prices.${priceIndex}.unitAmount`}
              render={({ field }) => (
                <FormItem>
                  <FormControl>
                    <Input {...field} aria-label={t("STEP_CREDITS.PRICE_PER_CREDIT")} type="number" min={0} step="any" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => prices.remove(priceIndex)}
              aria-label={t("STEP_CREDITS.REMOVE_PRICE")}
            >
              {t("common:REMOVE")}
            </Button>
          </div>
        ))}
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => prices.append({ currencyCode: "", unitAmount: 0 })}
        >
          {t("STEP_CREDITS.ADD_CURRENCY")}
        </Button>
        <FormField
          control={control}
          name={`meterCredits.${index}.prices`}
          render={() => (
            <FormItem>
              <FormMessage />
            </FormItem>
          )}
        />
        <p className="text-xs text-muted-foreground">
          {t("STEP_CREDITS.CURRENCY_HINT")}
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <FormField
          control={control}
          name={`meterCredits.${index}.minQuantity`}
          render={({ field }) => (
            <FormItem>
              <FormLabel className="text-xs">{t("STEP_CREDITS.SMALLEST_PURCHASE")}</FormLabel>
              <FormControl>
                <Input {...field} type="number" min={1} step={1} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name={`meterCredits.${index}.maxQuantity`}
          render={({ field }) => (
            <FormItem>
              <FormLabel className="text-xs">{t("STEP_CREDITS.LARGEST_PURCHASE")}</FormLabel>
              <FormControl>
                <Input {...field} value={field.value ?? ""} type="number" min={1} step={1} />
              </FormControl>
              <p className="text-xs text-muted-foreground">{t("STEP_CREDITS.LARGEST_PURCHASE_HINT")}</p>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name={`meterCredits.${index}.expiryPolicy`}
          render={({ field }) => (
            <FormItem>
              <FormLabel className="text-xs">{t("STEP_CREDITS.CREDITS_EXPIRE")}</FormLabel>
              <Select
                value={String(field.value)}
                onValueChange={(value) => field.onChange(Number(value))}
              >
                <FormControl>
                  <SelectTrigger aria-label={t("STEP_CREDITS.CREDITS_EXPIRE")}>
                    <SelectValue />
                  </SelectTrigger>
                </FormControl>
                <SelectContent>
                  {expiryOptions.map((option) => (
                    <SelectItem key={option.value} value={String(option.value)}>
                      {option.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                {expiryOptions.find((option) => option.value === expiry)?.description}
              </p>
              <FormMessage />
            </FormItem>
          )}
        />
      </div>

      {expiry === FIXED_CREDIT_EXPIRY && (
        <div className="grid grid-cols-[8rem_minmax(0,1fr)] gap-2 sm:max-w-sm">
          <FormField
            control={control}
            name={`meterCredits.${index}.expiresAfterCount`}
            render={({ field }) => (
              <FormItem>
                <FormLabel className="text-xs">{t("STEP_CREDITS.CREDITS_LAST")}</FormLabel>
                <FormControl>
                  <Input {...field} value={field.value ?? ""} aria-label={t("STEP_CREDITS.CREDITS_LAST")} type="number" min={1} step={1} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={control}
            name={`meterCredits.${index}.expiresAfterUnit`}
            render={({ field }) => (
              <FormItem>
                <FormLabel className="text-xs">&nbsp;</FormLabel>
                <Select value={String(field.value)} onValueChange={(value) => field.onChange(Number(value))}>
                  <FormControl>
                    <SelectTrigger aria-label={t("STEP_CREDITS.LIFETIME_UNIT")}>
                      <SelectValue />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {unitOptions.map((option) => (
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
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <FormField
          control={control}
          name={`meterCredits.${index}.taxPercent`}
          render={({ field }) => (
            <FormItem>
              <FormLabel className="text-xs">{t("STEP_CREDITS.TAX_RATE")}</FormLabel>
              <FormControl>
                <Input {...field} value={field.value ?? ""} type="number" min={0} max={100} step="any" />
              </FormControl>
              <p className="text-xs text-muted-foreground">{t("STEP_CREDITS.TAX_RATE_HINT")}</p>
              <FormMessage />
            </FormItem>
          )}
        />
        {Boolean(taxPercent) && (
          <FormField
            control={control}
            name={`meterCredits.${index}.taxMode`}
            render={({ field }) => (
              <FormItem>
                <FormLabel className="text-xs">{t("STEP_CREDITS.TAX_IS")}</FormLabel>
                <Select value={field.value} onValueChange={field.onChange}>
                  <FormControl>
                    <SelectTrigger aria-label={t("STEP_CREDITS.TAX_IS")}>
                      <SelectValue />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {TAX_MODE_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormMessage />
              </FormItem>
            )}
          />
        )}
      </div>
    </div>
  );
};
