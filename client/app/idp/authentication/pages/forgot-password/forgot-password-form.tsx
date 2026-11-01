import { Button } from "@/components/ui-kits/button/button";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { Input } from "@/components/ui-kits/input/input";
import { Link } from "react-router";
import { useForm } from "react-hook-form";
import { createForgotPasswordFormSchema, forgotPasswordFormDefaultValue } from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { z } from "zod";
import { showErrorToast } from "@/hooks/use-toast";
import { useNavigate } from "react-router";
import { Captcha } from "@/components/captcha";
import { useEffect } from "react";
import { useAccountRecover } from "@blocks-idp/iam/hooks/use-account";
import { isErrorWithErrors } from "@/lib/error";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { useT } from "@seliseblocks/genesis-os";

export const ForgotPasswordForm = () => {
  const { t } = useT("utilities.idp.authentication");
  const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");

  const navigate = useNavigate();
  const form = useForm({
    defaultValues: forgotPasswordFormDefaultValue,
    resolver: zodResolver(createForgotPasswordFormSchema()),
  });
  const { isPending, mutateAsync } = useAccountRecover();
  const googleSiteKey = getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const {
    captcha,
    code: captchaCode,
    reset: resetCaptcha,
  } = useCaptcha({
    siteKey: googleSiteKey,
    type: "reCaptcha-v2-checkbox",
  });
  const { isValid } = form.formState;

  const onSubmitHandler = async (values: z.infer<ReturnType<typeof createForgotPasswordFormSchema>>) => {
    try {
      if (!x_blocks_key) return;
      const res = await mutateAsync({
        ...values,
        captchaCode,
        projectKey: x_blocks_key,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        return showErrorToast({ errors: res.errors });
      }
      navigate(`/forgot-email-sent?email=${values.email}`);
    } catch (error) {
      resetCaptcha();
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: t("FORGOT_PASSWORD_FORM.SOMETHING_WENT_WRONG") });
    }
  };

  useEffect(() => {
    if (!isValid && captchaCode) resetCaptcha();
  }, [captchaCode, isValid, resetCaptcha]);

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmitHandler)}>
        <div className="grid grid-cols-1 gap-4">
          <FormField
            control={form.control}
            name="email"
            render={({ field }) => (
              <FormItem>
                <FormLabel>{t("common:EMAIL")}</FormLabel>
                <FormControl>
                  <Input type="email" placeholder={t("FORGOT_PASSWORD_FORM.ENTER_YOUR_EMAIL")} {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          {isValid && <Captcha {...captcha} />}

          <div className="my-2 flex items-center">
            <hr className="flex-grow border-gray-300" />
            <span className="mx-2 text-xs text-gray-500">{t("FORGOT_PASSWORD_FORM.OR")}</span>
            <hr className="flex-grow border-gray-300" />
          </div>

          <Button
            type="submit"
            className="w-full rounded"
            disabled={isPending || !isValid || !captchaCode}
          >
            {t("common:CONTINUE")}
          </Button>
        </div>
        <div className="mt-4 text-center text-base text-foreground">
          {t("FORGOT_PASSWORD_FORM.ALREADY_A_MEMBER")}{" "}
          <Link to={"/login"} className="text-primary hover:underline">
            {t("FORGOT_PASSWORD_FORM.LOG_IN")}
          </Link>
        </div>
      </form>
    </Form>
  );
};
