import { Button } from "@/components/ui-kits/button/button";
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
import { useState } from "react";
import { isErrorWithErrors } from "@/lib/error";
import { useOIDCContext } from "@/layouts/oidc-layout";
import { buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";
import { accountRecover } from "@blocks-idp/authentication/services/oidc-auth-flow.service";
import { useT } from "@seliseblocks/genesis-os";

export const OidcForgotPasswordForm = () => {
  const { t } = useT("utilities.idp.authentication");
  const { themeColor, projectKey } = useOIDCContext();
  const [isPending, setIsPending] = useState(false);

  const navigate = useNavigate();
  const form = useForm({
    defaultValues: forgotPasswordFormDefaultValue,
    resolver: zodResolver(createForgotPasswordFormSchema()),
  });

  const { isValid } = form.formState;

  const onSubmitHandler = async (values: z.infer<ReturnType<typeof createForgotPasswordFormSchema>>) => {
    try {
      if (!projectKey) return;
      setIsPending(true);
      const res = await accountRecover({
        email: values.email,
        projectKey: projectKey,
      });
      if (!res?.isSuccess) {
        return showErrorToast({ errors: res?.error || t("OIDC_FORGOT_PASSWORD_FORM.FAILED_TO_RECOVER_ACCOUNT") });
      }
      const baseUrl = buildOIDCNavigationUrl(`/oidc/email-sent-confirmation`);
      const navUrl = `${baseUrl}&email=${encodeURIComponent(values.email)}`;
      navigate(navUrl);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: t("OIDC_FORGOT_PASSWORD_FORM.SOMETHING_WENT_WRONG") });
    } finally {
      setIsPending(false);
    }
  };

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
                  <Input type="email" placeholder={t("OIDC_FORGOT_PASSWORD_FORM.ENTER_YOUR_EMAIL")} {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />

          <Button
            type="submit"
            className="w-full rounded"
            style={{ backgroundColor: themeColor }}
            disabled={isPending || !isValid}
          >
            {t("common:CONTINUE")}
          </Button>
        </div>
        <div className="mt-4 text-center text-base text-foreground">
          {t("OIDC_FORGOT_PASSWORD_FORM.GO_BACK_TO")}{" "}
          <Link
            to={buildOIDCNavigationUrl("/oidc/login")}
            className="hover:underline"
            style={{ color: themeColor }}
          >
            {t("OIDC_FORGOT_PASSWORD_FORM.LOG_IN")}
          </Link>
        </div>
      </form>
    </Form>
  );
};
