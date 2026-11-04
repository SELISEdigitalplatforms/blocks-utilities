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
import { getCaptchaGeneratorType } from "../../models/captcha";
import { useT } from "@seliseblocks/genesis-os";

type ConfigureBlockCaptchaFormProps = {
  form: UseFormReturn<typeof ConfigureCaptchaFormDefaultValue>;
};

export const ConfigureBlockCaptchaFormField = ({ form }: ConfigureBlockCaptchaFormProps) => {
  const { t } = useT("utilities.idp");
  return (
    <>
      <FormField
        name="captchaGenerator"
        control={form.control}
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("CONFIGURE_BLOCK_CAPTCHA_FORM_FIELD.CAPTCHA_GENERATOR_TYPE")}</FormLabel>
            <FormControl>
              <Select onValueChange={field.onChange} defaultValue={field.value}>
                <SelectTrigger>
                  <SelectValue placeholder={t("CONFIGURE_BLOCK_CAPTCHA_FORM_FIELD.SELECT_TYPE")} />
                </SelectTrigger>
                <SelectContent>
                  {Object.values(getCaptchaGeneratorType()).map((item) => (
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
