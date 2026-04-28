import { UseFormReturn } from "react-hook-form";
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { ConfigureCaptchaFormDefaultValue } from "./utils";
import { CAPTCHA_GENERATOR_TYPE } from "../../models/captcha";

type ConfigureBlockCaptchaFormProps = {
  form: UseFormReturn<typeof ConfigureCaptchaFormDefaultValue>;
};

export const ConfigureBlockCaptchaFormField = ({ form }: ConfigureBlockCaptchaFormProps) => {
  return (
    <>
      <FormField
        name="captchaGenerator"
        control={form.control}
        render={({ field }) => (
          <FormItem>
            <FormLabel>CAPTCHA Generator type</FormLabel>
            <FormControl>
              <Select onValueChange={field.onChange} defaultValue={field.value}>
                <SelectTrigger>
                  <SelectValue placeholder="Select type" />
                </SelectTrigger>
                <SelectContent>
                  {Object.values(CAPTCHA_GENERATOR_TYPE).map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
    </>
  );
};

ConfigureBlockCaptchaFormField.displayName = "Blocks";
