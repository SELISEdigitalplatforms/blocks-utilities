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
  getPaymentMethodOptions,
} from "../constants/payment.constants";
import { useT } from "@seliseblocks/genesis-os";

interface ProviderConfigurationFields {
  frontendResultUrl: string;
  countryCode: string;
  manualCapture: boolean;
  captureDelayHours: string;
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
  const { t } = useT("utilities.payment");
  const form = useFormContext<ProviderConfigurationFields>();
  const selectedMethods = useWatch({
    control: form.control,
    name: "checkoutPaymentMethodTypes",
  });
  const hasSelectedMethods = (selectedMethods ?? []).length > 0;

  const methodOptions = getPaymentMethodOptions();
  const offered: PaymentMethodChoice[] = [
    ...methodOptions,
    ...[...new Set(additionalPaymentMethods)]
      .filter(
        (method) =>
          !methodOptions.some((option) => option.value === method),
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
            <FormLabel>{t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.FRONTEND_RESULT_URL")}</FormLabel>
            <FormControl>
              <Input
                {...field}
                type="url"
                // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
                placeholder="https://app.example.com/payment/result"
                autoComplete="url"
              />
            </FormControl>
            <FormDescription>
              {t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.THE_APPLICATION_PAGE_THAT_RECEIVES")}
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
            <FormLabel>{t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.COUNTRY_CODE")}</FormLabel>
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
              {t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.OPTIONAL_TWO_LETTER_ISO_COUNTRY")}
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
            <FormLabel>{t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.STORE_ID")}</FormLabel>
            <FormControl>
              <Input
                {...field}
                value={field.value ?? ""}
                maxLength={200}
                placeholder={t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.OPTIONAL_PROVIDER_STORE")}
                autoComplete="off"
              />
            </FormControl>
            <FormDescription>
              {t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.ONLY_SET_THIS_WHEN_THE")}
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
            <FormLabel>{t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.MAXIMUM_REFUND_AGE")}</FormLabel>
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
              {t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.NUMBER_OF_DAYS_AFTER_PAYMENT")}
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
              <FormLabel>{t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.MANUAL_CAPTURE")}</FormLabel>
              <FormDescription>
                {t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.AUTHORIZE_FIRST_AND_CAPTURE")}
              </FormDescription>
              <FormMessage />
            </div>
            <FormControl>
              <Switch
                checked={field.value}
                onCheckedChange={field.onChange}
                aria-label={t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.ENABLE_MANUAL_CAPTURE")}
              />
            </FormControl>
          </FormItem>
        )}
      />

      {providerName === "ADYEN-ONLINE" && (
        <FormField
          control={form.control}
          name="captureDelayHours"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.CAPTURE_DELAY_HOURS")}</FormLabel>
              <FormControl>
                <Input
                  name={field.name}
                  ref={field.ref}
                  value={field.value ?? ""}
                  type="number"
                  min="0"
                  max="168"
                  step="1"
                  placeholder={t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.NOT_SET")}
                  onBlur={field.onBlur}
                  onChange={(event) => field.onChange(event.target.value)}
                />
              </FormControl>
              <FormDescription>
                {t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.ENTER_0_WHEN_THE_ADYEN")}
              </FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />
      )}

      {providerName === "STRIPE" && (
        <div className="space-y-5 rounded-xl border p-4 sm:col-span-2">
          <div>
            <h3 className="font-semibold">{t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.CHECKOUT_PAYMENT_METHODS")}</h3>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.WHICH_METHODS_STRIPE_CHECKOUT")}
            </p>
          </div>

          <FormField
            control={form.control}
            name="checkoutPaymentMethodTypes"
            render={({ field }) => (
              <FormItem>
                <FormLabel>{t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.OFFER_THESE_METHODS")}</FormLabel>
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
                              {t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.ONE_OFF_PAYMENTS_ONLY")}
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
                  {t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.LEAVE_EVERY_BOX_UNCHECKED_TO")}
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
                <FormLabel>{t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.PAYMENT_METHOD_CONFIGURATION_ID")}</FormLabel>
                <FormControl>
                  <Input
                    {...field}
                    value={field.value ?? ""}
                    maxLength={100}
                    // eslint-disable-next-line no-restricted-syntax -- example value shown as a placeholder, not prose
                    placeholder="pmc_…"
                    autoComplete="off"
                    spellCheck={false}
                    disabled={hasSelectedMethods}
                  />
                </FormControl>
                <FormDescription>
                  {hasSelectedMethods
                    ? t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.IGNORED_WHILE_METHODS_ARE_TICKED")
                    : t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.OPTIONAL_A_CONFIGURATION_ASSEMBLED")}
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
                <FormLabel>{t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.PROVIDER_ENABLED")}</FormLabel>
                <FormDescription>
                  {t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.DISABLED_PROVIDERS_CANNOT_BE")}
                </FormDescription>
                <FormMessage />
              </div>
              <FormControl>
                <Switch
                  checked={field.value}
                  onCheckedChange={field.onChange}
                  aria-label={t("PAYMENT_PROVIDER_CONFIGURATION_FIELDS.ENABLE_PAYMENT_PROVIDER")}
                />
              </FormControl>
            </FormItem>
          )}
        />
      )}
    </div>
  );
};
