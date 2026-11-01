import { useForm } from "react-hook-form";
import { getRuntimeEnv } from "@/lib/runtime-env";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Button } from "@/components/ui-kits/button/button";
import { PasswordInput } from "@/components/password-input";
import { z } from "zod";
import { useNavigate } from "react-router";
import { showErrorToast } from "@/hooks/use-toast";
import { useAccountResetPassword } from "@blocks-idp/iam/hooks/use-account";
import { Captcha } from "@/components/captcha";
import { useEffect, useState } from "react";
import { isErrorWithErrors } from "@/lib/error";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { PasswordStrengthChecker } from "@blocks-idp/authentication/components/password-strength-checker/password-strength-checker";
import { zodResolver } from "@hookform/resolvers/zod";
import { activationFormDefaultValue, createActivationFormSchema } from "../activation/utils";
import { useT } from "@seliseblocks/genesis-os";

type ResetPasswordFormProps = {
  code: string;
};

const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");

export const ResetPasswordForm = ({ code }: ResetPasswordFormProps) => {
  const { t } = useT("utilities.idp.authentication");
  const navigate = useNavigate();
  const form = useForm({
    defaultValues: activationFormDefaultValue,
    mode: "all",
    reValidateMode: "onChange",
    resolver: zodResolver(createActivationFormSchema()),
  });
  const [requirementsMet, setRequirementsMet] = useState(false);

  const googleSiteKey = getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const {
    captcha,
    code: captchaCode,
    reset: resetCaptcha,
  } = useCaptcha({ siteKey: googleSiteKey, type: "reCaptcha-v2-checkbox" });

  const { isPending, mutateAsync } = useAccountResetPassword();

  const { isValid } = form.formState;

  useEffect(() => {
    if (!isValid && !requirementsMet && captchaCode) resetCaptcha();
  }, [captchaCode, isValid, requirementsMet, resetCaptcha]);

  const onSubmitHandler = async (values: z.infer<ReturnType<typeof createActivationFormSchema>>) => {
    try {
      const res = await mutateAsync({
        code: code,
        captchaCode,
        logoutFromAllDevices: true,
        projectKey: x_blocks_key || "",
        password: values.password,
      });

      if (!res.isSuccess) {
        resetCaptcha();
        return showErrorToast({ errors: res.errors });
      }
      navigate("/reset-password-success");
    } catch (error: unknown) {
      resetCaptcha();
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: t("RESET_PASSWORD_FORM.SOMETHING_WENT_WRONG") });
    }
  };

  const password = form.watch("password");
  const confirmPassword = form.watch("confirmPassword");

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmitHandler)} className="flex flex-col gap-4">
        <FormField
          control={form.control}
          name="password"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("RESET_PASSWORD_FORM.PASSWORD")}</FormLabel>
              <FormControl>
                <PasswordInput {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={form.control}
          name="confirmPassword"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("RESET_PASSWORD_FORM.CONFIRM_PASSWORD")}</FormLabel>
              <FormControl>
                <PasswordInput {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <PasswordStrengthChecker
          password={password}
          confirmPassword={confirmPassword}
          onRequirementsMet={setRequirementsMet}
        />

        {isValid && requirementsMet && <Captcha {...captcha} />}

        <Button
          type="submit"
          className="w-full"
          disabled={isPending || !captchaCode || !isValid || !requirementsMet}
        >
          {t("RESET_PASSWORD_FORM.RESET_PASSWORD")}
        </Button>
      </form>
    </Form>
  );
};
