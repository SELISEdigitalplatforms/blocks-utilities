import { Input } from "@/components/ui-kits/input/input";
import { UseFormReturn } from "react-hook-form";
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { ConfigureCaptchaFormDefaultValue } from "./utils";

type ConfigureGeneralCaptchaFormProps = {
  form: UseFormReturn<typeof ConfigureCaptchaFormDefaultValue>;
};

export const ConfigureGeneralCaptchaFormField = ({ form }: ConfigureGeneralCaptchaFormProps) => {
  return (
    <>
      <FormField
        name="captchaKey"
        control={form.control}
        render={({ field }) => (
          <FormItem>
            <FormLabel>Site key</FormLabel>
            <FormControl>
              <Input placeholder="Enter site key" {...field} />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        name="captchaSecret"
        control={form.control}
        render={({ field }) => (
          <FormItem>
            <FormLabel>Secret key</FormLabel>
            <FormControl>
              <Input placeholder="Enter secret key" {...field} />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
    </>
  );
};
