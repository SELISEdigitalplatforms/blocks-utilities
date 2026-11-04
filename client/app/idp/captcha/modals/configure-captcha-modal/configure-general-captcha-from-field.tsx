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
import { useT } from "@seliseblocks/genesis-os";

type ConfigureGeneralCaptchaFormProps = {
  form: UseFormReturn<typeof ConfigureCaptchaFormDefaultValue>;
};

export const ConfigureGeneralCaptchaFormField = ({ form }: ConfigureGeneralCaptchaFormProps) => {
  const { t } = useT("utilities.idp");
  return (
    <>
      <FormField
        name="captchaKey"
        control={form.control}
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("CONFIGURE_GENERAL_CAPTCHA_FROM_FIELD.SITE_KEY")}</FormLabel>
            <FormControl>
              <Input placeholder={t("CONFIGURE_GENERAL_CAPTCHA_FROM_FIELD.ENTER_SITE_KEY")} {...field} />
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
            <FormLabel>{t("CONFIGURE_GENERAL_CAPTCHA_FROM_FIELD.SECRET_KEY")}</FormLabel>
            <FormControl>
              <Input placeholder={t("CONFIGURE_GENERAL_CAPTCHA_FROM_FIELD.ENTER_SECRET_KEY")} {...field} />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
    </>
  );
};
