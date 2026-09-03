import { useFormContext, useWatch } from "react-hook-form";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import { Switch } from "@/components/ui-kits/switch/switch";
import {
  canBeReusedOffSession,
  PAYMENT_METHOD_OPTIONS,
} from "../constants/payment.constants";

interface ProviderConfigurationFields {
  frontendResultUrl: string;
  countryCode: string;
  manualCapture: boolean;
  maxRefundDays: number;
  storeId?: string;
  isEnabled?: boolean;
  checkoutPaymentMethodTypes: string[];
  paymentMethodConfigurationId?: string;
}

interface PaymentProviderConfigurationFieldsProps {
  includeEnabled?: boolean;
  /**
   * Which provider is being configured. The payment method selection is Stripe's own concept and
   * is hidden for anything else; Adyen ignores both fields entirely.
   */
  providerName?: string;
  /**
   * The methods already stored on this provider. Any that the curated list does not offer are
   * added to it, so they stay visible and can be kept or removed deliberately.
   *
   * These two fields are settable through the API, where nothing constrains them to the list
   * below. Ticking any box rewrites the whole list from what is offered here, so a method that
   * was not offered would be dropped by an edit that had nothing to do with it — a silent change
   * to how a live checkout behaves. Read from the stored provider rather than from form state,
   * so unticking one does not make its checkbox vanish mid-edit.
   */
  additionalPaymentMethods?: readonly string[];
}

/** One method the form can offer; labelled by its raw value when it is not a curated one. */
interface PaymentMethodChoice {
  value: string;
  label: string;
  hint?: string | undefined;
}

export const PaymentProviderConfigurationFields = ({
  includeEnabled = false,
  providerName,
  additionalPaymentMethods = [],
}: PaymentProviderConfigurationFieldsProps) => {
  const form = useFormContext<ProviderConfigurationFields>();
  const selectedMethods = useWatch({
    control: form.control,
    name: "checkoutPaymentMethodTypes",
  });
  const hasSelectedMethods = (selectedMethods ?? []).length > 0;

  const offered: PaymentMethodChoice[] = [
    ...PAYMENT_METHOD_OPTIONS,
    ...[...new Set(additionalPaymentMethods)]
      .filter(
        (method) =>
          !PAYMENT_METHOD_OPTIONS.some((option) => option.value === method),
      )
      .map((method) => ({ value: method, label: method })),
  ];

  return (
    <div className="grid gap-5 sm:grid-cols-2">
      <FormField
        control={form.control}
        name="frontendResultUrl"
        render={({ field }) => (
          <FormItem className="sm:col-span-2">
            <FormLabel>Frontend result URL</FormLabel>
            <FormControl>
              <Input
                {...field}
                type="url"
                placeholder="https://app.example.com/payment/result"
                autoComplete="url"
              />
            </FormControl>
            <FormDescription>
              The application page that receives the safe payment result.
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />

      <FormField
        control={form.control}
        name="countryCode"
        render={({ field }) => (
          <FormItem>
            <FormLabel>Country code</FormLabel>
            <FormControl>
              <Input
                {...field}
                value={field.value ?? ""}
                maxLength={2}
                placeholder="CH"
                className="uppercase"
                autoComplete="country"
              />
            </FormControl>
            <FormDescription>
              Optional two-letter ISO country code.
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />

      <FormField
        control={form.control}
        name="storeId"
        render={({ field }) => (
          <FormItem>
            <FormLabel>Store ID</FormLabel>
            <FormControl>
              <Input
                {...field}
                value={field.value ?? ""}
                maxLength={200}
                placeholder="Optional provider store"
                autoComplete="off"
              />
            </FormControl>
            <FormDescription>
              Only set this when the provider account uses a store.
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />

      <FormField
        control={form.control}
        name="maxRefundDays"
        render={({ field }) => (
          <FormItem>
            <FormLabel>Maximum refund age</FormLabel>
            <FormControl>
              <Input
                name={field.name}
                ref={field.ref}
                value={field.value}
                type="number"
                min="0"
                max="3650"
                step="1"
                onBlur={field.onBlur}
                onChange={(event) =>
                  field.onChange(event.target.valueAsNumber)
                }
              />
            </FormControl>
            <FormDescription>
              Number of days after payment; 0 disables the age limit.
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />

      <FormField
        control={form.control}
        name="manualCapture"
        render={({ field }) => (
          <FormItem className="flex items-start justify-between gap-4 rounded-xl border p-4">
            <div>
              <FormLabel>Manual capture</FormLabel>
              <FormDescription>
                Authorize first and capture through the payment API later.
              </FormDescription>
              <FormMessage />
            </div>
            <FormControl>
              <Switch
                checked={field.value}
                onCheckedChange={field.onChange}
                aria-label="Enable manual capture"
              />
            </FormControl>
          </FormItem>
        )}
      />

      {providerName === "STRIPE" && (
        <div className="space-y-5 rounded-xl border p-4 sm:col-span-2">
          <div>
            <h3 className="font-semibold">Checkout payment methods</h3>
            <p className="mt-1 text-sm text-muted-foreground">
              Which methods Stripe Checkout offers a shopper.
            </p>
          </div>

          <FormField
            control={form.control}
            name="checkoutPaymentMethodTypes"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Offer these methods</FormLabel>
                <div className="space-y-3 rounded-md border p-3">
                  {offered.map((option) => {
                    const selected = (field.value ?? []).includes(
                      option.value,
                    );

                    return (
                      <div key={option.value} className="space-y-1">
                        <div className="flex items-center justify-between gap-2">
                          {/* The label wraps the control so the method name is its accessible
                              name. The badge and hint stay outside it, or they would be read as
                              part of that name. */}
                          <label className="flex cursor-pointer items-center gap-2 text-sm">
                            <Checkbox
                              checked={selected}
                              onCheckedChange={(checked) => {
                                const chosen = new Set(field.value ?? []);

                                if (checked) {
                                  chosen.add(option.value);
                                } else {
                                  chosen.delete(option.value);
                                }

                                // Rebuilt in the order shown rather than in the order ticked:
                                // Stripe renders the methods in the order they arrive, and a
                                // checkbox list gives no sign of click order, so ordering by
                                // what is on screen is the only version an operator can predict.
                                field.onChange(
                                  offered
                                    .map((candidate) => candidate.value)
                                    .filter((value) => chosen.has(value)),
                                );
                              }}
                            />
                            {option.label}
                          </label>
                          {!canBeReusedOffSession(option.value) && (
                            <Badge variant="outline">
                              one-off payments only
                            </Badge>
                          )}
                        </div>
                        {option.hint && (
                          <p className="pl-6 text-xs text-muted-foreground">
                            {option.hint}
                          </p>
                        )}
                      </div>
                    );
                  })}
                </div>
                <FormDescription>
                  Leave every box unchecked to offer whatever the Stripe
                  Dashboard enables, which is what this provider does today. A
                  method marked one-off cannot be charged again later, so Stripe
                  leaves it off a subscription&rsquo;s first payment even when it
                  is ticked here — it still appears on ordinary payments.
                </FormDescription>
                <FormMessage />
              </FormItem>
            )}
          />

          <FormField
            control={form.control}
            name="paymentMethodConfigurationId"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Payment method configuration ID</FormLabel>
                <FormControl>
                  <Input
                    {...field}
                    value={field.value ?? ""}
                    maxLength={100}
                    placeholder="pmc_…"
                    autoComplete="off"
                    spellCheck={false}
                    disabled={hasSelectedMethods}
                  />
                </FormControl>
                <FormDescription>
                  {hasSelectedMethods
                    ? "Ignored while methods are ticked above — Stripe rejects a checkout that names both, so the ticked list wins. Untick them all to use this instead."
                    : "Optional. A configuration assembled in the Stripe Dashboard, used when no method is ticked above."}
                </FormDescription>
                <FormMessage />
              </FormItem>
            )}
          />
        </div>
      )}

      {includeEnabled && (
        <FormField
          control={form.control}
          name="isEnabled"
          render={({ field }) => (
            <FormItem className="flex items-start justify-between gap-4 rounded-xl border p-4 sm:col-span-2">
              <div>
                <FormLabel>Provider enabled</FormLabel>
                <FormDescription>
                  Disabled providers cannot be selected for new payments.
                </FormDescription>
                <FormMessage />
              </div>
              <FormControl>
                <Switch
                  checked={field.value}
                  onCheckedChange={field.onChange}
                  aria-label="Enable payment provider"
                />
              </FormControl>
            </FormItem>
          )}
        />
      )}
    </div>
  );
};
