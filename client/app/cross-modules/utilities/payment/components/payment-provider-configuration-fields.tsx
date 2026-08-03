import { useFormContext } from "react-hook-form";
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

interface ProviderConfigurationFields {
  frontendResultUrl: string;
  countryCode: string;
  manualCapture: boolean;
  maxRefundDays: number;
  storeId?: string;
  isEnabled?: boolean;
}

interface PaymentProviderConfigurationFieldsProps {
  includeEnabled?: boolean;
}

export const PaymentProviderConfigurationFields = ({
  includeEnabled = false,
}: PaymentProviderConfigurationFieldsProps) => {
  const form = useFormContext<ProviderConfigurationFields>();

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
